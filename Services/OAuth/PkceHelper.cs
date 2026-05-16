// <copyright file="PkceHelper.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.Services.OAuth;

/// <summary>
///     Generates PKCE (Proof Key for Code Exchange) code verifier and challenge values
///     using the S256 method defined in RFC 7636.
/// </summary>
public static class PkceHelper
{
    /// <summary>
    ///     Generates a new PKCE pair.
    /// </summary>
    /// <returns>
    ///     (verifier, challenge) — send the verifier with the token exchange,
    ///     send the challenge with the authorization request.
    /// </returns>
    public static (string Verifier, string Challenge) Generate()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(verifierBytes);

        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(challengeBytes);

        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
               .TrimEnd('=')
               .Replace('+', '-')
               .Replace('/', '_');
}
