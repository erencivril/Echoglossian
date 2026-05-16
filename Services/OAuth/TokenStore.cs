// <copyright file="TokenStore.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.Services.OAuth;

/// <summary>
///     Persists and retrieves OAuth tokens using Windows DPAPI so that refresh tokens
///     are never stored in plain text. Tokens are bound to the current Windows user
///     account and can only be decrypted by that account on that machine.
/// </summary>
public sealed class TokenStore
{
    private readonly string storeDirectory;

    public TokenStore(IDalamudPluginInterface pluginInterface)
    {
        this.storeDirectory = Path.Combine(
            pluginInterface.GetPluginConfigDirectory(),
            "oauth");
        Directory.CreateDirectory(this.storeDirectory);
    }

    public void Save(OAuthProvider provider, OAuthTokens tokens)
    {
        var json = JsonConvert.SerializeObject(tokens, Formatting.Indented);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);

        var tmpPath = this.FilePath(provider) + ".tmp";
        File.WriteAllBytes(tmpPath, encrypted);
        File.Move(tmpPath, this.FilePath(provider), overwrite: true);
    }

    public OAuthTokens? Load(OAuthProvider provider)
    {
        var path = this.FilePath(provider);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var encrypted = File.ReadAllBytes(path);
            var jsonBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(jsonBytes);
            return JsonConvert.DeserializeObject<OAuthTokens>(json);
        }
        catch
        {
            // Corrupted or from a different user profile — treat as missing.
            return null;
        }
    }

    public void Delete(OAuthProvider provider)
    {
        var path = this.FilePath(provider);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string FilePath(OAuthProvider provider) =>
        Path.Combine(this.storeDirectory, $"{provider.ToString().ToLowerInvariant()}.dat");
}
