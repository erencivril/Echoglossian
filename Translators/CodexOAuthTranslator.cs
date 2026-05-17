// <copyright file="CodexOAuthTranslator.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

using Echoglossian.Translators.Helpers;

namespace Echoglossian.Translators;

/// <summary>
///     Translates game text using the OpenAI Codex backend authenticated via OAuth
///     (ChatGPT Plus/Pro subscription — no per-token API billing).
///     Uses the <c>chatgpt.com/backend-api/codex/responses</c> SSE endpoint.
/// </summary>
public class CodexOAuthTranslator : ITranslator
{
    private const string ResponsesEndpoint = "https://chatgpt.com/backend-api/codex/responses";

    private readonly HttpClient httpClient;
    private readonly TimeSpan initialBackoff = TimeSpan.FromSeconds(1);
    private readonly int maxRetries = 3;
    private readonly string model;
    private readonly IOAuthTokenProvider oauthProvider;
    private readonly IPluginLog pluginLog;
    private readonly ConcurrentTranslationRequestCache translationCache = new();

    public CodexOAuthTranslator(IPluginLog pluginLog, Config config, IOAuthTokenProvider oauthProvider)
    {
        this.pluginLog = pluginLog;
        this.oauthProvider = oauthProvider;
        this.model = config.CodexOAuthModel ?? "gpt-4o";

        this.httpClient = new HttpClient();
        this.httpClient.DefaultRequestHeaders.Add("User-Agent", "codex-cli/0.1.2505161131");
    }

    public string Translate(string text, string sourceLanguage, string targetLanguage) =>
        this.TranslateAsync(text, sourceLanguage, targetLanguage).GetAwaiter().GetResult() ?? string.Empty;

    public async Task<string?> TranslateAsync(string text, string sourceLanguage, string targetLanguage)
    {
        if (!this.oauthProvider.IsSignedIn(OAuthProvider.OpenAI))
        {
            return Resources.CodexOAuthNotSignedIn;
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
        var systemPrompt = BuildSystemPrompt(sourceLanguage, targetLanguage);
        var userMessage = $"Translate this text: \"{fixedText}\"";

        var requestBody = new
        {
            model = this.model,
            instructions = systemPrompt,
            input = userMessage,
        };

        var jsonContent = JsonConvert.SerializeObject(requestBody);

        for (var retry = 0; retry <= this.maxRetries; retry++)
        {
            try
            {
                var accessToken = await this.oauthProvider.GetAccessTokenAsync(OAuthProvider.OpenAI)
                                                          .ConfigureAwait(false);

                using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Required for chatgpt.com/backend-api: pin the workspace from the JWT.
                var accountId = this.oauthProvider.GetChatGptAccountId(OAuthProvider.OpenAI);
                if (!string.IsNullOrEmpty(accountId))
                {
                    request.Headers.Add("ChatGPT-Account-ID", accountId);
                }

                request.Headers.Add("OpenAI-Beta", "responses=experimental");
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await this.httpClient.SendAsync(request).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && retry == 0)
                {
                    PluginRuntimeLog.Warning(
                        this.pluginLog,
                        "[CodexOAuth] Received 401 — will retry with refreshed token.");
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    PluginRuntimeLog.Warning(
                        this.pluginLog,
                        "[CodexOAuth] Rate limited (429). Switch to another model in Settings.");
                    return Resources.CodexOAuthRateLimited;
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
                        $"[CodexOAuth] Request failed after retries: {response.StatusCode}");
                    return $"[{Resources.TranslationError} CodexOAuth {response.StatusCode}]";
                }

                var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var responseObject = JObject.Parse(responseString);

                // Responses API: output[0].content[0].text
                var translatedText = responseObject["output"]?[0]?["content"]?[0]?["text"]
                    ?.Value<string>()?.Trim();

                if (string.IsNullOrEmpty(translatedText))
                {
                    PluginRuntimeLog.Error(this.pluginLog, "[CodexOAuth] SSE stream yielded no text.");
                    return $"[{Resources.TranslationError} CodexOAuth empty response]";
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
                        $"[CodexOAuth] HTTP error: {httpEx.Message}. Retry {retry + 1}/{this.maxRetries}.");
                    await Task.Delay(backoff).ConfigureAwait(false);
                }
                else
                {
                    PluginRuntimeLog.Error(this.pluginLog, $"[CodexOAuth] HTTP error: {httpEx.Message}");
                    return $"[{Resources.TranslationError} {httpEx.Message}]";
                }
            }
            catch (InvalidOperationException opEx)
            {
                PluginRuntimeLog.Warning(this.pluginLog, $"[CodexOAuth] Not signed in: {opEx.Message}");
                return Resources.CodexOAuthNotSignedIn;
            }
            catch (Exception ex)
            {
                PluginRuntimeLog.Error(this.pluginLog, $"[CodexOAuth] Unexpected error: {ex.Message}");
                return $"[{Resources.TranslationError} {ex.Message}]";
            }
        }

        return string.Empty;
    }

    private static string BuildSystemPrompt(string sourceLanguage, string targetLanguage) =>
        $"You are an expert translator and cultural localization specialist for Final Fantasy XIV. " +
        $"Translate the provided text from {sourceLanguage} to {targetLanguage}. " +
        $"Preserve the original tone, personality, and FFXIV-specific terminology. " +
        $"Respond with only the translated text — no explanations, no quotation marks.";
}
