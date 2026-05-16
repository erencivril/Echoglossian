// <copyright file="OAuthLoopbackServer.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.Services.OAuth;

/// <summary>
///     Spins up a short-lived localhost HTTP listener to capture the OAuth authorization
///     code that the provider redirects back after the user signs in.
/// </summary>
public sealed class OAuthLoopbackServer : IDisposable
{
    private readonly HttpListener listener;
    private bool disposed;

    public OAuthLoopbackServer(string callbackPath = "/oauth2callback", int? fixedPort = null, int? fallbackPort = null)
    {
        this.Port = fixedPort.HasValue ? TryBindFixed(fixedPort.Value, fallbackPort) : GetFreePort();
        this.RedirectUri = $"http://localhost:{this.Port}{callbackPath}";

        this.listener = new HttpListener();
        this.listener.Prefixes.Add($"http://localhost:{this.Port}/");
        this.listener.Start();
    }

    public int Port { get; }

    public string RedirectUri { get; }

    /// <summary>
    ///     Waits for the browser to hit the callback URL and extracts
    ///     the authorization code (and optional state parameter).
    /// </summary>
    public async Task<(string Code, string? State)> WaitForCallbackAsync(
        CancellationToken cancellationToken)
    {
        using var reg = cancellationToken.Register(() => this.listener.Stop());

        HttpListenerContext context;
        try
        {
            context = await this.listener.GetContextAsync().ConfigureAwait(false);
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OAuthCancelledException();
        }

        var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
        var code = query["code"];
        var state = query["state"];
        var error = query["error"];

        // Send a polite HTML page so the user can close the tab.
        var html = string.IsNullOrEmpty(error)
            ? "<html><body><h2>Echoglossian: Sign-in successful!</h2><p>You can close this tab and return to the game.</p></body></html>"
            : $"<html><body><h2>Sign-in failed</h2><p>{HttpUtility.HtmlEncode(error)}</p></body></html>";

        var responseBytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = responseBytes.Length;
        await context.Response.OutputStream.WriteAsync(responseBytes, cancellationToken).ConfigureAwait(false);
        context.Response.OutputStream.Close();

        if (!string.IsNullOrEmpty(error))
        {
            throw new OAuthException($"OAuth provider returned error: {error}");
        }

        if (string.IsNullOrEmpty(code))
        {
            throw new OAuthException("OAuth callback did not contain an authorization code.");
        }

        return (code, state);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        try
        {
            this.listener.Stop();
        }
        catch
        {
            // Ignore errors on dispose.
        }

        this.listener.Close();
    }

    private static int GetFreePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    private static int TryBindFixed(int preferred, int? fallback)
    {
        try
        {
            using var tcp = new TcpListener(IPAddress.Loopback, preferred);
            tcp.Start();
            tcp.Stop();
            return preferred;
        }
        catch (SocketException) when (fallback.HasValue)
        {
            return fallback.Value;
        }
    }
}
