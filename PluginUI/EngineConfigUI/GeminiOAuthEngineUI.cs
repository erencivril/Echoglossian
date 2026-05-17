// <copyright file="GeminiOAuthEngineUI.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

using Echoglossian.PluginUI.Components;
using Echoglossian.Services.OAuth;
using Echoglossian.Translators.GeminiOAuth;

namespace Echoglossian.PluginUI.EngineConfigUI;

/// <summary>
///     UI panel for configuring the GeminiOAuth (Google Gemini via OAuth) translation engine.
/// </summary>
public static class GeminiOAuthEngineUI
{
    private static bool signingIn;
    private static string? signInError;
    private static bool testingConnection;
    private static string? testConnectionResult;

    /// <summary>
    ///     Draws the GeminiOAuth configuration panel.
    /// </summary>
    /// <param name="config">The active plugin configuration.</param>
    /// <param name="promptManager">The shared prompt template manager.</param>
    /// <returns><see langword="true"/> when any setting changed.</returns>
    public static bool Draw(Config config, PromptTemplateManager promptManager)
    {
        var changed = false;

        ImGui.TextWrapped(Resources.SettingsForGeminiOAuthText);
        ImGui.Spacing();

        var tokenProvider = Echoglossian.OAuthTokenProvider;
        var isSignedIn = tokenProvider?.IsSignedIn(OAuthProvider.Google) ?? false;
        var accountEmail = tokenProvider?.GetAccountEmail(OAuthProvider.Google);

        // ── Sign-in status ───────────────────────────────────────────────────
        if (isSignedIn && !string.IsNullOrWhiteSpace(accountEmail))
        {
            ImGui.TextColored(
                new Vector4(0.4f, 1f, 0.4f, 1f),
                string.Format(
                    Resources.ResourceManager.GetString("GeminiOAuthSignedInAs", Resources.Culture) ??
                    "Signed in as: {0}",
                    accountEmail));

            ImGui.SameLine();

            if (ImGui.Button(
                    Resources.ResourceManager.GetString("GeminiOAuthSignOutButton", Resources.Culture) ??
                    "Sign out"))
            {
                tokenProvider!.SignOut(OAuthProvider.Google);
                config.GeminiOAuthAccountEmail = string.Empty;
                signInError = null;
                testConnectionResult = null;
                changed = true;
                Echoglossian.SaveConfig(config);
            }
        }
        else
        {
            ImGui.TextColored(
                new Vector4(1f, 0.8f, 0.4f, 1f),
                Resources.ResourceManager.GetString("GeminiOAuthNotSignedIn", Resources.Culture) ??
                "Not signed in. Click the button below to authenticate with Google.");

            ImGui.Spacing();

            if (signingIn)
            {
                ImGui.TextDisabled(
                    Resources.ResourceManager.GetString("GeminiOAuthSigningIn", Resources.Culture) ??
                    "Waiting for browser authentication...");
            }
            else
            {
                if (ImGui.Button(
                        Resources.ResourceManager.GetString("GeminiOAuthSignInButton", Resources.Culture) ??
                        "Sign in with Google"))
                {
                    if (tokenProvider != null)
                    {
                        signingIn = true;
                        signInError = null;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var email = await tokenProvider.SignInAsync(
                                    OAuthProvider.Google,
                                    CancellationToken.None);
                                config.GeminiOAuthAccountEmail = email;
                                Echoglossian.SaveConfig(config);
                            }
                            catch (Exception ex)
                            {
                                signInError = ex.Message;
                            }
                            finally
                            {
                                signingIn = false;
                            }
                        });
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(signInError))
            {
                ImGui.TextColored(
                    new Vector4(1f, 0.4f, 0.4f, 1f),
                    string.Format(
                        Resources.ResourceManager.GetString("GeminiOAuthSignInError", Resources.Culture) ??
                        "Sign-in failed: {0}",
                        signInError));
            }
        }

        ImGui.Separator();
        ImGui.Spacing();

        // ── Auto-routing toggle ──────────────────────────────────────────────
        var autoRoute = config.GeminiOAuthAutoRouteByLength;
        if (ImGui.Checkbox("Auto-select model by text length (recommended)", ref autoRoute))
        {
            config.GeminiOAuthAutoRouteByLength = autoRoute;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Short text (<50 chars) -> Flash Lite (fastest, cheapest)\n" +
                "Medium text (<250 chars) -> Flash\n" +
                "Long text (>=250 chars) -> the model picked below");
        }

        ImGui.Spacing();

        // ── Model selection ──────────────────────────────────────────────────
        var models = GeminiOAuthTextModelDefaults.PredefinedModels;
        var modelId = config.GeminiOAuthModel ?? GeminiOAuthTextModelDefaults.DefaultModelId;

        var dropdownLabel = autoRoute
            ? "Model for long passages (>=250 chars)"
            : Resources.LLMModel;

        if (ModelDropdownUI.Draw(
                dropdownLabel,
                ref modelId,
                models,
                "GeminiOAuth"))
        {
            config.GeminiOAuthModel = modelId;
            changed = true;
        }

        ImGui.Spacing();

        // ── Test connection ──────────────────────────────────────────────────
        if (isSignedIn)
        {
            if (testingConnection)
            {
                ImGui.TextDisabled(
                    Resources.ResourceManager.GetString("GeminiOAuthTestingConnection", Resources.Culture) ??
                    "Testing connection...");
            }
            else
            {
                if (ImGui.Button(
                        Resources.ResourceManager.GetString("GeminiOAuthTestConnection", Resources.Culture) ??
                        "Test connection"))
                {
                    testingConnection = true;
                    testConnectionResult = null;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var translator = new GeminiOAuthTranslator(
                                Echoglossian.PluginLog,
                                config,
                                tokenProvider!);
                            var result = await translator.TranslateAsync("Hello", "en", "ja");
                            testConnectionResult = string.IsNullOrWhiteSpace(result)
                                ? Resources.ResourceManager.GetString("GeminiOAuthTestFailed", Resources.Culture) ?? "Test failed: empty response."
                                : string.Format(
                                    Resources.ResourceManager.GetString("GeminiOAuthTestOk", Resources.Culture) ??
                                    "OK — translated: {0}",
                                    result);
                        }
                        catch (Exception ex)
                        {
                            testConnectionResult = string.Format(
                                Resources.ResourceManager.GetString("GeminiOAuthTestFailed", Resources.Culture) ??
                                "Test failed: {0}",
                                ex.Message);
                        }
                        finally
                        {
                            testingConnection = false;
                        }
                    });
                }

                if (!string.IsNullOrWhiteSpace(testConnectionResult))
                {
                    var isOk = testConnectionResult!.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
                    ImGui.TextColored(
                        isOk ? new Vector4(0.4f, 1f, 0.4f, 1f) : new Vector4(1f, 0.4f, 0.4f, 1f),
                        testConnectionResult);
                }
            }
        }

        if (changed)
        {
            Echoglossian.SaveConfig(config);
        }

        return changed;
    }
}
