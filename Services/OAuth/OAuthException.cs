// <copyright file="OAuthException.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.Services.OAuth;

/// <summary>
///     Raised when an OAuth flow fails.
/// </summary>
public class OAuthException : Exception
{
    public OAuthException(string message)
        : base(message)
    {
    }

    public OAuthException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
///     Raised when the user cancels the OAuth sign-in flow (e.g. closes the browser).
/// </summary>
public sealed class OAuthCancelledException : OAuthException
{
    public OAuthCancelledException()
        : base("Sign-in was cancelled.")
    {
    }
}

/// <summary>
///     Raised when a token refresh fails and the user must sign in again.
/// </summary>
public sealed class OAuthRefreshFailedException : OAuthException
{
    public OAuthRefreshFailedException(string message)
        : base(message)
    {
    }

    public OAuthRefreshFailedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
