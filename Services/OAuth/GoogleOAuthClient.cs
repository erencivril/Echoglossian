// <copyright file="GoogleOAuthClient.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.Services.OAuth;

/// <summary>
///     Implements the Google OAuth 2.0 PKCE "installed app" flow used by gemini-cli.
///     The client_id/secret are the public "installed app" credentials embedded in the
///     official gemini-cli open-source project (google-gemini/gemini-cli).
/// </summary>
public sealed class GoogleOAuthClient
{
    // Installed-app credentials from gemini-cli (packages/core/src/code_assist/oauth2.ts).
    private const string ClientId =
        "681255809395-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com";

    private const string ClientSecret = "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";

    // Scopes required for Gemini Code Assist usage.
    private const string Scopes =
        "https://www.googleapis.com/auth/cloud-platform " +
        "https://www.googleapis.com/auth/userinfo.email " +
        "https://www.googleapis.com/auth/userinfo.profile";

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
            "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(server.RedirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(Scopes)}" +
            "&access_type=offline" +
            "&prompt=consent" +
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
            ["client_secret"] = ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = current.RefreshToken,
        });

        var response = await this.http.PostAsync(
            "https://oauth2.googleapis.com/token",
            body,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new OAuthRefreshFailedException(
                $"Google token refresh failed ({response.StatusCode}): {err}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var root = JObject.Parse(json);

        var expiresIn = root["expires_in"]?.Value<int>() ?? 3600;

        return new OAuthTokens
        {
            AccessToken = root["access_token"]?.Value<string>() ?? string.Empty,
            RefreshToken = current.RefreshToken, // Google does not always return a new refresh token
            IdToken = root["id_token"]?.Value<string>() ?? current.IdToken,
            TokenType = root["token_type"]?.Value<string>() ?? "Bearer",
            Scope = root["scope"]?.Value<string>() ?? current.Scope,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            AccountEmail = current.AccountEmail,
        };
    }

    /// <summary>
    ///     Fetches the signed-in user's e-mail from Google UserInfo endpoint.
    /// </summary>
    public async Task<string> FetchUserEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://www.googleapis.com/oauth2/v1/userinfo?alt=json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await this.http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var root = JObject.Parse(json);
        return root["email"]?.Value<string>() ?? string.Empty;
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
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        });

        var response = await this.http.PostAsync(
            "https://oauth2.googleapis.com/token",
            body,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new OAuthException($"Google token exchange failed ({response.StatusCode}): {err}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var root = JObject.Parse(json);

        var expiresIn = root["expires_in"]?.Value<int>() ?? 3600;

        return new OAuthTokens
        {
            AccessToken = root["access_token"]?.Value<string>() ?? string.Empty,
            RefreshToken = root["refresh_token"]?.Value<string>(),
            IdToken = root["id_token"]?.Value<string>(),
            TokenType = root["token_type"]?.Value<string>() ?? "Bearer",
            Scope = root["scope"]?.Value<string>(),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
        };
    }
}
