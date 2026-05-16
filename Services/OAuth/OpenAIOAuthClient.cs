// <copyright file="OpenAIOAuthClient.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.Services.OAuth;

/// <summary>
///     Implements the OpenAI OAuth 2.0 PKCE flow used by the official Codex CLI
///     (openai/codex). This allows users with a ChatGPT Plus/Pro subscription to use
///     Codex-family models without API key billing.
/// </summary>
public sealed class OpenAIOAuthClient
{
    // Public PKCE client from openai/codex (codex-rs/login/src/server.rs).
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";

    private const string Scopes =
        "openai profile email offline_access api.connectors.read api.connectors.invoke";

    private readonly HttpClient http = new();

    /// <summary>
    ///     Runs the full loopback PKCE authorization flow:
    ///     opens the system browser, waits for the redirect, and exchanges the code.
    /// </summary>
    public async Task<OAuthTokens> AuthorizeAsync(CancellationToken cancellationToken)
    {
        using var server = new OAuthLoopbackServer();
        var (verifier, challenge) = PkceHelper.Generate();
        var stateBytes = RandomNumberGenerator.GetBytes(16);
        var state = Convert.ToBase64String(stateBytes).Replace("+", "").Replace("/", "").Replace("=", "");
        state = state.Length >= 16 ? state[..16] : state;

        var authUrl =
            "https://auth.openai.com/oauth/authorize" +
            $"?client_id={Uri.EscapeDataString(ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(server.RedirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(Scopes)}" +
            $"&code_challenge={challenge}" +
            "&code_challenge_method=S256" +
            $"&state={state}";

        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var (code, returnedState) = await server.WaitForCallbackAsync(linked.Token).ConfigureAwait(false);

        if (returnedState != state)
        {
            throw new OAuthException("OAuth state mismatch — possible CSRF attack.");
        }

        return await this.ExchangeCodeAsync(code, verifier, server.RedirectUri, cancellationToken)
                         .ConfigureAwait(false);
    }

    /// <summary>
    ///     Refreshes the access token using the stored refresh token.
    /// </summary>
    public async Task<OAuthTokens> RefreshAsync(OAuthTokens current, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(current.RefreshToken))
        {
            throw new OAuthRefreshFailedException("No refresh token stored — please sign in again.");
        }

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = current.RefreshToken,
            ["scope"] = Scopes,
        });

        var response = await this.http.PostAsync(
            "https://auth.openai.com/oauth/token",
            body,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new OAuthRefreshFailedException(
                $"OpenAI token refresh failed ({response.StatusCode}): {err}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseTokenResponse(json, current.RefreshToken, current.AccountEmail);
    }

    /// <summary>
    ///     Decodes the id_token JWT payload to extract the account e-mail.
    ///     No signature verification is needed here — we trust our own token.
    /// </summary>
    public static string? ExtractEmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken))
        {
            return null;
        }

        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = parts[1];
            // Base64url → Base64 padding
            var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var bytes = Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/'));
            var jsonStr = Encoding.UTF8.GetString(bytes);
            var doc = JObject.Parse(jsonStr);
            return doc["email"]?.Value<string>();
        }
        catch
        {
            return null;
        }
    }

    private async Task<OAuthTokens> ExchangeCodeAsync(
        string code,
        string verifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        });

        var response = await this.http.PostAsync(
            "https://auth.openai.com/oauth/token",
            body,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new OAuthException($"OpenAI token exchange failed ({response.StatusCode}): {err}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseTokenResponse(json, null, null);
    }

    private static OAuthTokens ParseTokenResponse(string json, string? fallbackRefresh, string? fallbackEmail)
    {
        var root = JObject.Parse(json);

        var expiresIn = root["expires_in"]?.Value<int>() ?? 3600;
        var refreshToken = root["refresh_token"]?.Value<string>() ?? fallbackRefresh;
        var idToken = root["id_token"]?.Value<string>();
        var email = ExtractEmailFromIdToken(idToken) ?? fallbackEmail;

        return new OAuthTokens
        {
            AccessToken = root["access_token"]?.Value<string>() ?? string.Empty,
            RefreshToken = refreshToken,
            IdToken = idToken,
            TokenType = root["token_type"]?.Value<string>() ?? "Bearer",
            Scope = root["scope"]?.Value<string>(),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            AccountEmail = email,
        };
    }
}
