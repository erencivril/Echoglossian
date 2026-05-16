// <copyright file="CodeAssistProjectResolver.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.Translators.GeminiOAuth;

/// <summary>
///     Resolves the Gemini Code Assist cloudaicompanionProject ID for the signed-in user.
///     Calls :loadCodeAssist once; caches the result in Config to avoid redundant network trips.
///     If the user has no project (first-time), calls :onboardUser with the free tier.
/// </summary>
internal sealed class CodeAssistProjectResolver
{
    private const string BaseUrl = "https://cloudcode-pa.googleapis.com/v1internal";

    private readonly Config config;
    private readonly HttpClient http;
    private readonly IPluginLog log;

    internal CodeAssistProjectResolver(IPluginLog log, Config config, HttpClient http)
    {
        this.log = log;
        this.config = config;
        this.http = http;
    }

    /// <summary>
    ///     Returns the cloudaicompanionProject string required for Code Assist API calls.
    ///     Uses cached value when available; otherwise discovers/provisions via the API.
    /// </summary>
    internal async Task<string> ResolveProjectIdAsync(string accessToken, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(this.config.GeminiOAuthCachedProjectId))
        {
            return this.config.GeminiOAuthCachedProjectId;
        }

        PluginRuntimeLog.Information(this.log, "[GeminiOAuth] Resolving Code Assist project ID...");

        var projectId = await this.LoadProjectIdAsync(accessToken, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(projectId))
        {
            PluginRuntimeLog.Information(this.log, "[GeminiOAuth] No project found — onboarding to free tier...");
            projectId = await this.OnboardAndGetProjectIdAsync(accessToken, ct).ConfigureAwait(false);
        }

        this.config.GeminiOAuthCachedProjectId = projectId;
        PluginRuntimeLog.Information(this.log, $"[GeminiOAuth] Code Assist project resolved: {projectId}");
        return projectId;
    }

    internal void ClearCache() => this.config.GeminiOAuthCachedProjectId = null;

    private async Task<string?> LoadProjectIdAsync(string accessToken, CancellationToken ct)
    {
        var body = JsonConvert.SerializeObject(new
        {
            metadata = new
            {
                ideType = "IDE_UNSPECIFIED",
                platform = "PLATFORM_UNSPECIFIED",
                pluginType = "GEMINI",
            },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}:loadCodeAssist");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await this.http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            PluginRuntimeLog.Warning(
                this.log,
                $"[GeminiOAuth] loadCodeAssist returned {response.StatusCode}.");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var root = JObject.Parse(json);
        return root["cloudaicompanionProject"]?.Value<string>();
    }

    private async Task<string> OnboardAndGetProjectIdAsync(string accessToken, CancellationToken ct)
    {
        // First: discover the default free tier ID.
        var loadBody = JsonConvert.SerializeObject(new
        {
            metadata = new { ideType = "IDE_UNSPECIFIED", platform = "PLATFORM_UNSPECIFIED", pluginType = "GEMINI" },
        });
        using var loadReq = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}:loadCodeAssist");
        loadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        loadReq.Content = new StringContent(loadBody, Encoding.UTF8, "application/json");

        var loadResp = await this.http.SendAsync(loadReq, ct).ConfigureAwait(false);
        loadResp.EnsureSuccessStatusCode();
        var loadJson = await loadResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var loadRoot = JObject.Parse(loadJson);

        var tierId = loadRoot["allowedTiers"]
            ?.FirstOrDefault(t => t["isDefault"]?.Value<bool>() == true)
            ?["id"]?.Value<string>() ?? "free-tier-gca";

        // Onboard the user with that tier.
        var onboardBody = JsonConvert.SerializeObject(new
        {
            tierId,
            metadata = new { ideType = "IDE_UNSPECIFIED", platform = "PLATFORM_UNSPECIFIED", pluginType = "GEMINI" },
        });
        using var onboardReq = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}:onboardUser");
        onboardReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        onboardReq.Content = new StringContent(onboardBody, Encoding.UTF8, "application/json");

        var onboardResp = await this.http.SendAsync(onboardReq, ct).ConfigureAwait(false);
        onboardResp.EnsureSuccessStatusCode();
        var onboardJson = await onboardResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var onboardRoot = JObject.Parse(onboardJson);

        // Poll until the long-running operation completes (max 30s).
        var operationName = onboardRoot["name"]?.Value<string>();
        if (!string.IsNullOrEmpty(operationName))
        {
            onboardRoot = await this.PollOperationAsync(accessToken, operationName, ct).ConfigureAwait(false);
        }

        return onboardRoot["response"]?["cloudaicompanionProject"]?.Value<string>()
            ?? onboardRoot["cloudaicompanionProject"]?.Value<string>()
            ?? string.Empty;
    }

    private async Task<JObject> PollOperationAsync(string accessToken, string operationName, CancellationToken ct)
    {
        for (var i = 0; i < 15; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);

            using var pollReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://cloudcode-pa.googleapis.com/{operationName}");
            pollReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var pollResp = await this.http.SendAsync(pollReq, ct).ConfigureAwait(false);
            if (!pollResp.IsSuccessStatusCode)
            {
                continue;
            }

            var pollJson = await pollResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var pollRoot = JObject.Parse(pollJson);

            if (pollRoot["done"]?.Value<bool>() == true)
            {
                return pollRoot;
            }
        }

        PluginRuntimeLog.Warning(this.log, "[GeminiOAuth] Onboarding operation timed out.");
        return new JObject();
    }
}
