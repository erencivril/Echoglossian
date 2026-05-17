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
    private readonly bool autoRouteByLength;
    private readonly IOAuthTokenProvider oauthProvider;
    private readonly IPluginLog pluginLog;
    private readonly float temperature = 0.1f;
    private readonly ConcurrentTranslationRequestCache translationCache = new();
    private readonly CodeAssistProjectResolver projectResolver;

    // Gemini Code Assist endpoint — required for personal Google accounts (OAuth tokens).
    // Standard generativelanguage.googleapis.com returns 403 for these tokens.
    // We use :streamGenerateContent (SSE) instead of :generateContent because only the
    // streaming variant accepts enabled_credit_types, which is what unlocks the
    // Google AI Pro / Google One AI paid quota — see gemini-cli's server.ts.
    private const string GenerateContentUrl =
        "https://cloudcode-pa.googleapis.com/v1internal:streamGenerateContent?alt=sse";

    // CreditType that lets Google AI Pro / Google One AI subscribers use their paid
    // quota instead of the strict Code Assist free tier (gemini-cli/billing.ts).
    private const string GoogleOneAiCreditType = "GOOGLE_ONE_AI";

    public GeminiOAuthTranslator(IPluginLog pluginLog, Config config, IOAuthTokenProvider oauthProvider)
    {
        this.pluginLog = pluginLog;
        this.oauthProvider = oauthProvider;
        this.model = config.GeminiOAuthModel ?? "gemini-3.1-pro-preview";
        this.autoRouteByLength = config.GeminiOAuthAutoRouteByLength;
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

        var effectiveModel = GeminiOAuthModelRouter.Route(text, this.model, this.autoRouteByLength);
        var cacheKey = $"{text}_{sourceLanguage}_{targetLanguage}_{effectiveModel}";
        if (this.translationCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        return await this.translationCache.GetOrAddAsync(
            cacheKey,
            () => this.TranslateCoreAsync(text, sourceLanguage, targetLanguage, effectiveModel, cacheKey))
            .ConfigureAwait(false);
    }

    private async Task<string?> TranslateCoreAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string effectiveModel,
        string cacheKey)
    {
        var fixedText = FixText(text);
        var prompt = BuildTranslationPrompt(fixedText, sourceLanguage, targetLanguage);

        if (!string.Equals(effectiveModel, this.model, StringComparison.Ordinal))
        {
            PluginRuntimeLog.Debug(
                this.pluginLog,
                $"[GeminiOAuth] Routed {text.Length}-char text to {effectiveModel} (selected: {this.model}).");
        }

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
                    model = effectiveModel,
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
                    // Unlocks paid Google AI Pro quota (gemini-cli passes this on every stream call).
                    enabled_credit_types = new[] { GoogleOneAiCreditType },
                };
                var jsonContent = JsonConvert.SerializeObject(requestBody);

                using var request = new HttpRequestMessage(HttpMethod.Post, GenerateContentUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await this.httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

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
                    var errBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var snippet = errBody.Length > 400 ? errBody[..400] : errBody;
                    PluginRuntimeLog.Error(
                        this.pluginLog,
                        $"[GeminiOAuth] {response.StatusCode}: {snippet}");

                    if (retry < this.maxRetries
                        && response.StatusCode >= System.Net.HttpStatusCode.InternalServerError)
                    {
                        var backoff = this.initialBackoff * Math.Pow(2, retry);
                        await Task.Delay(backoff).ConfigureAwait(false);
                        continue;
                    }

                    return $"[{Resources.TranslationError} GeminiOAuth {response.StatusCode}]";
                }

                var translatedText = await ReadCodeAssistSseAsync(response).ConfigureAwait(false);

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
        $"You are a Final Fantasy XIV in-game text translator. Translate the following text from " +
        $"{sourceLanguage} to {targetLanguage}. Preserve names, terminology, tone, and formal/informal " +
        $"speech. Respond with only the translated text, no quotes or commentary.\n\nText: {text}";

    /// <summary>
    ///     Reads Code Assist :streamGenerateContent SSE stream and accumulates
    ///     text from every chunk's response.candidates[0].content.parts[0].text.
    /// </summary>
    private static async Task<string> ReadCodeAssistSseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var sb = new StringBuilder();
        var dataBuffer = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line == null)
            {
                break;
            }

            if (line.Length == 0)
            {
                // End of event — parse accumulated data.
                if (dataBuffer.Length > 0)
                {
                    AppendCandidatesText(sb, dataBuffer.ToString());
                    dataBuffer.Clear();
                }
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                dataBuffer.Append(line["data: ".Length..]);
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataBuffer.Append(line["data:".Length..]);
            }
        }

        // Flush trailing event (no terminating blank line).
        if (dataBuffer.Length > 0)
        {
            AppendCandidatesText(sb, dataBuffer.ToString());
        }

        return sb.ToString().Trim();
    }

    private static void AppendCandidatesText(StringBuilder sink, string json)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException)
        {
            return;
        }

        // Code Assist envelope: { response: { candidates: [ { content: { parts: [{text}] } } ] } }
        var candidatesNode = root["response"]?["candidates"] ?? root["candidates"];
        if (candidatesNode is not JArray candidates)
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            var parts = candidate["content"]?["parts"] as JArray;
            if (parts == null)
            {
                continue;
            }

            foreach (var part in parts)
            {
                var text = part["text"]?.Value<string>();
                if (!string.IsNullOrEmpty(text))
                {
                    sink.Append(text);
                }
            }
        }
    }
}
