// <copyright file="CodexOAuthEngineUI.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

using Echoglossian.PluginUI.Components;
using Echoglossian.Services.OAuth;
using Echoglossian.Translators.CodexOAuth;

namespace Echoglossian.PluginUI.EngineConfigUI;

/// <summary>
///     UI panel for configuring the CodexOAuth (OpenAI Codex via OAuth) translation engine.
/// </summary>
public static class CodexOAuthEngineUI
{
    private static bool signingIn;
    private static string? signInError;
    private static bool testingConnection;
    private static string? testConnectionResult;

    /// <summary>
    ///     Draws the CodexOAuth configuration panel.
    /// </summary>
    /// <param name="config">The active plugin configuration.</param>
    /// <param name="promptManager">The shared prompt template manager.</param>
    /// <returns><see langword="true"/> when any setting changed.</returns>
    public static bool Draw(Config config, PromptTemplateManager promptManager)
    {
        var changed = false;

        ImGui.TextWrapped(Resources.SettingsForCodexOAuthText);
        ImGui.Spacing();

        var tokenProvider = Echoglossian.OAuthTokenProvider;
        var isSignedIn = tokenProvider?.IsSignedIn(OAuthProvider.OpenAI) ?? false;
        var accountEmail = tokenProvider?.GetAccountEmail(OAuthProvider.OpenAI);

        // ── Sign-in status ───────────────────────────────────────────────────
        if (isSignedIn && !string.IsNullOrWhiteSpace(accountEmail))
        {
            ImGui.TextColored(
                new Vector4(0.4f, 1f, 0.4f, 1f),
                string.Format(
                    Resources.ResourceManager.GetString("CodexOAuthSignedInAs", Resources.Culture) ??
                    "Signed in as: {0}",
                    accountEmail));

            ImGui.SameLine();

            if (ImGui.Button(
                    Resources.ResourceManager.GetString("CodexOAuthSignOutButton", Resources.Culture) ??
                    "Sign out"))
            {
                tokenProvider!.SignOut(OAuthProvider.OpenAI);
                config.CodexOAuthAccountEmail = string.Empty;
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
                Resources.ResourceManager.GetString("CodexOAuthNotSignedIn", Resources.Culture) ??
                "Not signed in. Click the button below to authenticate with OpenAI.");

            ImGui.Spacing();

            if (signingIn)
            {
                ImGui.TextDisabled(
                    Resources.ResourceManager.GetString("CodexOAuthSigningIn", Resources.Culture) ??
                    "Waiting for browser authentication...");
            }
            else
            {
                if (ImGui.Button(
                        Resources.ResourceManager.GetString("CodexOAuthSignInButton", Resources.Culture) ??
                        "Sign in with OpenAI"))
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
                                    OAuthProvider.OpenAI,
                                    CancellationToken.None);
                                config.CodexOAuthAccountEmail = email;
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
                        Resources.ResourceManager.GetString("CodexOAuthSignInError", Resources.Culture) ??
                        "Sign-in failed: {0}",
                        signInError));
            }
        }

        ImGui.Separator();
        ImGui.Spacing();

        // ── Model selection ──────────────────────────────────────────────────
        var models = CodexOAuthTextModelDefaults.PredefinedModels;
        var modelId = config.CodexOAuthModel ?? CodexOAuthTextModelDefaults.DefaultModelId;

        if (ModelDropdownUI.Draw(
                Resources.LLMModel,
                ref modelId,
                models,
                "CodexOAuth"))
        {
            config.CodexOAuthModel = modelId;
            changed = true;
        }

        ImGui.Spacing();

        // ── Prompt editor ────────────────────────────────────────────────────
        PromptEditorUI.Draw(
            promptManager,
            Echoglossian.PromptType.CodexOAuth,
            PromptTemplateManager.DefaultPrompt,
            Echoglossian.TransEngines.CodexOAuth.ToString());

        ImGui.Spacing();

        // ── Test connection ──────────────────────────────────────────────────
        if (isSignedIn)
        {
            if (testingConnection)
            {
                ImGui.TextDisabled(
                    Resources.ResourceManager.GetString("CodexOAuthTestingConnection", Resources.Culture) ??
                    "Testing connection...");
            }
            else
            {
                if (ImGui.Button(
                        Resources.ResourceManager.GetString("CodexOAuthTestConnection", Resources.Culture) ??
                        "Test connection"))
                {
                    testingConnection = true;
                    testConnectionResult = null;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var translator = new CodexOAuthTranslator(
                                Echoglossian.PluginLog,
                                config,
                                tokenProvider!);
                            var result = await translator.TranslateAsync("Hello", "en", "ja");
                            testConnectionResult = string.IsNullOrWhiteSpace(result)
                                ? Resources.ResourceManager.GetString("CodexOAuthTestFailed", Resources.Culture) ?? "Test failed: empty response."
                                : string.Format(
                                    Resources.ResourceManager.GetString("CodexOAuthTestOk", Resources.Culture) ??
                                    "OK — translated: {0}",
                                    result);
                        }
                        catch (Exception ex)
                        {
                            testConnectionResult = string.Format(
                                Resources.ResourceManager.GetString("CodexOAuthTestFailed", Resources.Culture) ??
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
