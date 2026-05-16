// <copyright file="GeminiOAuthTranslator.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

using Echoglossian.Translators.Helpers;

namespace Echoglossian.Translators;

/// <summary>
///     Translates game text using the Google Gemini API authenticated via OAuth
///     (Gemini Code Assist / Google AI Pro subscription — no per-token API billing).
/// </summary>
public class GeminiOAuthTranslator : ITranslator
{
    private readonly HttpClient httpClient;
    private readonly TimeSpan initialBackoff = TimeSpan.FromSeconds(1);
    private readonly int maxRetries = 3;
    private readonly string model;
    private readonly IOAuthTokenProvider oauthProvider;
    private readonly IPluginLog pluginLog;
    private readonly float temperature = 0.1f;
    private readonly ConcurrentTranslationRequestCache translationCache = new();
    private readonly CodeAssistProjectResolver projectResolver;

    // Gemini Code Assist endpoint — required for personal Google accounts (OAuth tokens).
    // Standard generativelanguage.googleapis.com returns 403 for these tokens.
    private const string GenerateContentUrl =
        "https://cloudcode-pa.googleapis.com/v1internal:generateContent";

    public GeminiOAuthTranslator(IPluginLog pluginLog, Config config, IOAuthTokenProvider oauthProvider)
    {
        this.pluginLog = pluginLog;
        this.oauthProvider = oauthProvider;
        this.model = config.GeminiOAuthModel ?? "gemini-2.5-flash";
        this.temperature = config.GeminiTemperature;

        this.httpClient = new HttpClient();
        this.httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        this.projectResolver = new CodeAssistProjectResolver(pluginLog, config, this.httpClient);
    }

    public string Translate(string text, string sourceLanguage, string targetLanguage) =>
        this.TranslateAsync(text, sourceLanguage, targetLanguage).GetAwaiter().GetResult() ?? string.Empty;

    public async Task<string?> TranslateAsync(string text, string sourceLanguage, string targetLanguage)
    {
        if (!this.oauthProvider.IsSignedIn(OAuthProvider.Google))
        {
            return Resources.GeminiOAuthNotSignedIn;
        }

        var cacheKey = $"{text}_{sourceLanguage}_{targetLanguage}_{this.model}";
        if (this.translationCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        return await this.translationCache.GetOrAddAsync(
            cacheKey,
            () => this.TranslateCoreAsync(text, sourceLanguage, targetLanguage, cacheKey))
            .ConfigureAwait(false);
    }

    private async Task<string?> TranslateCoreAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string cacheKey)
    {
        var fixedText = FixText(text);
        var prompt = BuildTranslationPrompt(fixedText, sourceLanguage, targetLanguage);

        // Request body built after we resolve the project ID (needs access token).
        // jsonContent is deferred inside the retry loop where we have the token.

        for (var retry = 0; retry <= this.maxRetries; retry++)
        {
            try
            {
                var accessToken = await this.oauthProvider.GetAccessTokenAsync(OAuthProvider.Google)
                                                          .ConfigureAwait(false);

                var projectId = await this.projectResolver
                                         .ResolveProjectIdAsync(accessToken, CancellationToken.None)
                                         .ConfigureAwait(false);

                var requestBody = new
                {
                    model = this.model,
                    project = projectId,
                    user_prompt_id = Guid.NewGuid().ToString(),
                    request = new
                    {
                        contents = new[]
                        {
                            new { role = "user", parts = new[] { new { text = prompt } } },
                        },
                        generationConfig = new { temperature = this.temperature },
                    },
                };
                var jsonContent = JsonConvert.SerializeObject(requestBody);

                using var request = new HttpRequestMessage(HttpMethod.Post, GenerateContentUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await this.httpClient.SendAsync(request).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && retry == 0)
                {
                    PluginRuntimeLog.Warning(
                        this.pluginLog,
                        "[GeminiOAuth] Received 401 — will retry with refreshed token.");
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && retry == 0)
                {
                    // Cached project ID may be stale — clear and retry once.
                    PluginRuntimeLog.Warning(
                        this.pluginLog,
                        "[GeminiOAuth] Received 403 — clearing cached project ID and retrying.");
                    this.projectResolver.ClearCache();
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    PluginRuntimeLog.Warning(
                        this.pluginLog,
                        "[GeminiOAuth] Rate limited (429). Switch to another model in Settings.");
                    return Resources.GeminiOAuthRateLimited;
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (retry < this.maxRetries)
                    {
                        var backoff = this.initialBackoff * Math.Pow(2, retry);
                        await Task.Delay(backoff).ConfigureAwait(false);
                        continue;
                    }

                    PluginRuntimeLog.Error(
                        this.pluginLog,
                        $"[GeminiOAuth] Request failed after retries: {response.StatusCode}");
                    return $"[{Resources.TranslationError} GeminiOAuth {response.StatusCode}]";
                }

                var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var responseObject = JObject.Parse(responseString);

                // Code Assist wraps the result in a "response" envelope.
                var candidatesNode = responseObject["response"]?["candidates"]
                    ?? responseObject["candidates"];
                var translatedText = candidatesNode?[0]?["content"]?["parts"]?[0]?["text"]
                    ?.ToString().Trim();

                if (string.IsNullOrEmpty(translatedText))
                {
                    PluginRuntimeLog.Error(this.pluginLog, "[GeminiOAuth] API returned empty text.");
                    return $"[{Resources.TranslationError} GeminiOAuth empty response]";
                }

                translatedText = FixText(translatedText.Trim('"'));
                if (TranslationResultGuard.IsPersistableTranslation(translatedText))
                {
                    this.translationCache.Remember(cacheKey, translatedText);
                }

                return translatedText;
            }
            catch (HttpRequestException httpEx)
            {
                if (retry < this.maxRetries)
                {
                    var backoff = this.initialBackoff * Math.Pow(2, retry);
                    PluginRuntimeLog.Warning(
                        this.pluginLog,
                        $"[GeminiOAuth] HTTP error: {httpEx.Message}. Retry {retry + 1}/{this.maxRetries}.");
                    await Task.Delay(backoff).ConfigureAwait(false);
                }
                else
                {
                    PluginRuntimeLog.Error(this.pluginLog, $"[GeminiOAuth] HTTP error: {httpEx.Message}");
                    return $"[{Resources.TranslationError} {httpEx.Message}]";
                }
            }
            catch (InvalidOperationException opEx)
            {
                // Not signed in (thrown by OAuthTokenProvider).
                PluginRuntimeLog.Warning(this.pluginLog, $"[GeminiOAuth] Not signed in: {opEx.Message}");
                return Resources.GeminiOAuthNotSignedIn;
            }
            catch (Exception ex)
            {
                PluginRuntimeLog.Error(this.pluginLog, $"[GeminiOAuth] Unexpected error: {ex.Message}");
                return $"[{Resources.TranslationError} {ex.Message}]";
            }
        }

        return string.Empty;
    }

    private static string BuildTranslationPrompt(string text, string sourceLanguage, string targetLanguage) =>
        @$"As an expert translator and cultural localization specialist with deep knowledge of video game localization, your task is to translate dialogues from the game Final Fantasy XIV from {sourceLanguage} to {targetLanguage}. This is not just a translation, but a full localization effort tailored for the Final Fantasy XIV universe. Please adhere to the following guidelines:

1. Preserve the original tone, humor, personality, and emotional nuances of the dialogue, considering the unique style and atmosphere of Final Fantasy XIV.
2. Adapt idioms, cultural references, and wordplay to resonate naturally with native {targetLanguage} speakers while maintaining the fantasy RPG context.
3. Maintain consistency in character voices, terminology, and naming conventions specific to Final Fantasy XIV throughout the translation.
4. Avoid literal translations that may lose the original intent or impact, especially for game-specific terms or lore elements.
5. Ensure the translation flows naturally and reads as if it were originally written in {targetLanguage}, while staying true to the game's narrative style.
6. Consider the context and subtext of the dialogue, including any references to the game's lore, world, or ongoing storylines.
7. If a word, phrase, or name has been translated in a specific way, maintain that translation consistently unless the context demands otherwise.
8. Pay attention to formal/informal speech patterns and adjust accordingly for the target language.
9. Preserve any game-specific jargon, spell names, or technical terms according to the official localization guidelines for Final Fantasy XIV.

Text to translate: ""{text}""

Please provide only the translated text in your response, without any explanations, additional comments, or quotation marks.";
}
