// <copyright file="OAuthTokenProvider.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.Services.OAuth;

/// <summary>
///     Central manager for OAuth tokens. Stores, refreshes, and hands out access tokens
///     for GeminiOAuth and CodexOAuth translation engines.
/// </summary>
public sealed class OAuthTokenProvider : IOAuthTokenProvider, IDisposable
{
    private readonly GoogleOAuthClient googleClient;
    private readonly IPluginLog log;
    private readonly OpenAIOAuthClient openAiClient;
    private readonly SemaphoreSlim googleRefreshLock = new(1, 1);
    private readonly SemaphoreSlim openAiRefreshLock = new(1, 1);
    private readonly TokenStore store;
    private readonly Dictionary<OAuthProvider, OAuthTokens?> tokenCache = new();
    private bool disposed;

    public OAuthTokenProvider(
        IPluginLog log,
        IDalamudPluginInterface pluginInterface)
    {
        this.log = log;
        this.store = new TokenStore(pluginInterface);
        this.googleClient = new GoogleOAuthClient();
        this.openAiClient = new OpenAIOAuthClient();

        // Warm the in-memory cache from disk.
        foreach (OAuthProvider provider in Enum.GetValues<OAuthProvider>())
        {
            this.tokenCache[provider] = this.store.Load(provider);
        }
    }

    /// <inheritdoc/>
    public async Task<string> GetAccessTokenAsync(
        OAuthProvider provider,
        CancellationToken cancellationToken = default)
    {
        var tokens = this.tokenCache.GetValueOrDefault(provider);
        if (tokens == null)
        {
            throw new InvalidOperationException(
                $"Not signed in to {provider}. Please sign in from the plugin settings.");
        }

        if (!tokens.IsExpiringSoon)
        {
            return tokens.AccessToken;
        }

        tokens = await this.RefreshAsync(provider, tokens, cancellationToken).ConfigureAwait(false);
        return tokens.AccessToken;
    }

    /// <inheritdoc/>
    public async Task<string> SignInAsync(
        OAuthProvider provider,
        CancellationToken cancellationToken = default)
    {
        PluginRuntimeLog.Information(this.log, $"[OAuthTokenProvider] Starting sign-in for {provider}.");

        OAuthTokens tokens = provider switch
        {
            OAuthProvider.Google => await this.googleClient.AuthorizeAsync(cancellationToken)
                                              .ConfigureAwait(false),
            OAuthProvider.OpenAI => await this.openAiClient.AuthorizeAsync(cancellationToken)
                                              .ConfigureAwait(false),
            _ => throw new NotSupportedException($"Unsupported provider: {provider}"),
        };

        // Fetch e-mail to display in UI.
        if (string.IsNullOrEmpty(tokens.AccountEmail))
        {
            try
            {
                var email = provider == OAuthProvider.Google
                    ? await this.googleClient.FetchUserEmailAsync(tokens.AccessToken, cancellationToken)
                                             .ConfigureAwait(false)
                    : OpenAIOAuthClient.ExtractEmailFromIdToken(tokens.IdToken) ?? string.Empty;

                tokens = tokens.WithEmail(email);
            }
            catch (Exception ex)
            {
                PluginRuntimeLog.Warning(this.log, $"[OAuthTokenProvider] Could not fetch e-mail: {ex.Message}");
            }
        }

        this.Persist(provider, tokens);
        PluginRuntimeLog.Information(this.log, $"[OAuthTokenProvider] Signed in to {provider} as {tokens.AccountEmail}.");

        if (provider == OAuthProvider.OpenAI)
        {
            if (string.IsNullOrEmpty(tokens.ChatGptAccountId))
            {
                PluginRuntimeLog.Warning(
                    this.log,
                    "[OAuthTokenProvider] Sign-in succeeded but no chatgpt_account_id was returned. " +
                    "Codex backend requires this — translation requests will fail.");
            }
            else
            {
                PluginRuntimeLog.Information(
                    this.log,
                    $"[OAuthTokenProvider] ChatGPT workspace ID resolved: {tokens.ChatGptAccountId}");
            }
        }

        return tokens.AccountEmail ?? string.Empty;
    }

    /// <inheritdoc/>
    public void SignOut(OAuthProvider provider)
    {
        this.tokenCache[provider] = null;
        this.store.Delete(provider);
        PluginRuntimeLog.Information(this.log, $"[OAuthTokenProvider] Signed out from {provider}.");
    }

    /// <inheritdoc/>
    public bool IsSignedIn(OAuthProvider provider) =>
        this.tokenCache.TryGetValue(provider, out var t) && t != null;

    /// <inheritdoc/>
    public string? GetAccountEmail(OAuthProvider provider) =>
        this.tokenCache.TryGetValue(provider, out var t) ? t?.AccountEmail : null;

    /// <inheritdoc/>
    public string? GetChatGptAccountId(OAuthProvider provider) =>
        this.tokenCache.TryGetValue(provider, out var t) ? t?.ChatGptAccountId : null;

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.googleRefreshLock.Dispose();
        this.openAiRefreshLock.Dispose();
    }

    private async Task<OAuthTokens> RefreshAsync(
        OAuthProvider provider,
        OAuthTokens current,
        CancellationToken cancellationToken)
    {
        var sem = provider == OAuthProvider.Google ? this.googleRefreshLock : this.openAiRefreshLock;

        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock — another thread may have already refreshed.
            var latest = this.tokenCache.GetValueOrDefault(provider);
            if (latest != null && !latest.IsExpiringSoon)
            {
                return latest;
            }

            PluginRuntimeLog.Debug(this.log, $"[OAuthTokenProvider] Refreshing token for {provider}.");

            var refreshed = provider switch
            {
                OAuthProvider.Google =>
                    await this.googleClient.RefreshAsync(current, cancellationToken).ConfigureAwait(false),
                OAuthProvider.OpenAI =>
                    await this.openAiClient.RefreshAsync(current, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Unsupported provider: {provider}"),
            };

            this.Persist(provider, refreshed);
            PluginRuntimeLog.Debug(this.log, $"[OAuthTokenProvider] Token refreshed for {provider}.");
            return refreshed;
        }
        catch (OAuthRefreshFailedException ex)
        {
            PluginRuntimeLog.Error(this.log, $"[OAuthTokenProvider] Refresh failed for {provider}: {ex.Message}");
            // Evict tokens so the UI shows "not signed in".
            this.tokenCache[provider] = null;
            throw;
        }
        finally
        {
            sem.Release();
        }
    }

    private void Persist(OAuthProvider provider, OAuthTokens tokens)
    {
        this.tokenCache[provider] = tokens;
        this.store.Save(provider, tokens);
    }
}
