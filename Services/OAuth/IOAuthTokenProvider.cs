// <copyright file="IOAuthTokenProvider.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.Services.OAuth;

/// <summary>
///     Provides valid access tokens for OAuth-backed translation engines,
///     handling the sign-in flow, token refresh, and persistence.
/// </summary>
public interface IOAuthTokenProvider
{
    /// <summary>
    ///     Returns a valid (non-expired) access token for the given provider.
    ///     Refreshes silently if the token is close to expiry.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no tokens are stored — the user must sign in first.
    /// </exception>
    /// <exception cref="OAuthRefreshFailedException">
    ///     Thrown when the refresh token is invalid or revoked.
    /// </exception>
    Task<string> GetAccessTokenAsync(OAuthProvider provider, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Opens the browser, runs the PKCE flow, stores the tokens, and fetches
    ///     the account e-mail. Must be called from a non-UI thread.
    /// </summary>
    Task<string> SignInAsync(OAuthProvider provider, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes stored tokens for the given provider.
    /// </summary>
    void SignOut(OAuthProvider provider);

    /// <summary>
    ///     Returns <see langword="true"/> if there are stored tokens for the given provider.
    /// </summary>
    bool IsSignedIn(OAuthProvider provider);

    /// <summary>
    ///     Returns the account e-mail associated with the stored tokens, or
    ///     <see langword="null"/> if not signed in.
    /// </summary>
    string? GetAccountEmail(OAuthProvider provider);
}
