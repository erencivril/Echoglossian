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
            "⚡ Gemini 2.5 Flash",
            true,
            false,
            true,
            true,
            IsDefault: true,
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
            "gemini-2.5-flash-lite",
            "💨 Gemini 2.5 Flash Lite",
            true,
            false,
            true,
            true,
            IsDefault: false,
            "GeminiOAuth"),
        new LlmTextModel(
            "gemini-3-flash",
            "🔷 Gemini 3 Flash",
            true,
            false,
            true,
            true,
            IsDefault: false,
            "GeminiOAuth"),
        new LlmTextModel(
            "gemini-3-pro",
            "🔮 Gemini 3 Pro",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "GeminiOAuth"),
    };
}
