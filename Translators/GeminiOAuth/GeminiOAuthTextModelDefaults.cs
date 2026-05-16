// <copyright file="GeminiOAuthTextModelDefaults.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

using Echoglossian.Translators.OpenAI;

namespace Echoglossian.Translators.GeminiOAuth;

public static class GeminiOAuthTextModelDefaults
{
    public const string DefaultModelId = "gemini-2.5-flash";

    public static readonly List<LlmTextModel> PredefinedModels = new()
    {
        new LlmTextModel(
            "gemini-2.5-flash",
            "⚡ Gemini 2.5 Flash [default]",
            true,
            false,
            true,
            true,
            IsDefault: true,
            "GeminiOAuth"),
        new LlmTextModel(
            "gemini-2.5-flash-lite",
            "💨 Gemini 2.5 Flash Lite",
            true,
            false,
            true,
            true,
            IsDefault: false,
            "GeminiOAuth"),
        new LlmTextModel(
            "gemini-2.5-pro",
            "🟢 Gemini 2.5 Pro",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "GeminiOAuth"),
        new LlmTextModel(
            "gemini-3.1-flash-lite",
            "🔷 Gemini 3.1 Flash Lite",
            true,
            false,
            true,
            true,
            IsDefault: false,
            "GeminiOAuth"),
        new LlmTextModel(
            "gemini-3-flash-preview",
            "🔶 Gemini 3 Flash (Preview)",
            true,
            false,
            true,
            true,
            IsDefault: false,
            "GeminiOAuth"),
        new LlmTextModel(
            "gemini-3.1-pro-preview",
            "🔮 Gemini 3.1 Pro (Preview)",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "GeminiOAuth"),
    };
}
