// <copyright file="OAuthTokens.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.Services.OAuth;

/// <summary>
///     Holds the tokens received after a successful OAuth authorization.
///     Serialized / deserialized via Newtonsoft.Json.
/// </summary>
public sealed class OAuthTokens
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonProperty("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonProperty("id_token")]
    public string? IdToken { get; set; }

    [JsonProperty("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonProperty("scope")]
    public string? Scope { get; set; }

    /// <summary>
    ///     UTC time when the access token expires.
    /// </summary>
    [JsonProperty("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    ///     Email address of the signed-in account (populated after fetching user info).
    /// </summary>
    [JsonProperty("account_email")]
    public string? AccountEmail { get; set; }

    /// <summary>
    ///     ChatGPT workspace/account ID extracted from the id_token's
    ///     <c>https://api.openai.com/auth.chatgpt_account_id</c> claim.
    ///     Required as <c>ChatGPT-Account-ID</c> header when calling the
    ///     <c>chatgpt.com/backend-api/codex/responses</c> endpoint.
    /// </summary>
    [JsonProperty("chatgpt_account_id")]
    public string? ChatGptAccountId { get; set; }

    [JsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow >= this.ExpiresAt;

    [JsonIgnore]
    public bool IsExpiringSoon => DateTimeOffset.UtcNow >= this.ExpiresAt - TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Returns a shallow copy with the account e-mail filled in.
    /// </summary>
    public OAuthTokens WithEmail(string email)
    {
        var copy = (OAuthTokens)this.MemberwiseClone();
        copy.AccountEmail = email;
        return copy;
    }
}
