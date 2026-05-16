// <copyright file="CodexOAuthTextModelDefaults.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

using Echoglossian.Translators.OpenAI;

namespace Echoglossian.Translators.CodexOAuth;

public static class CodexOAuthTextModelDefaults
{
    public const string DefaultModelId = "gpt-4o";

    public static readonly List<LlmTextModel> PredefinedModels = new()
    {
        new LlmTextModel(
            "gpt-4o",
            "⭐ GPT-4o [default]",
            true,
            false,
            true,
            false,
            IsDefault: true,
            "CodexOAuth"),
        new LlmTextModel(
            "gpt-4.1",
            "🤖 GPT-4.1",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "CodexOAuth"),
        new LlmTextModel(
            "gpt-4.1-mini",
            "⚡ GPT-4.1 Mini",
            true,
            false,
            true,
            true,
            IsDefault: false,
            "CodexOAuth"),
        new LlmTextModel(
            "o4-mini",
            "🔷 o4-mini",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "CodexOAuth"),
        new LlmTextModel(
            "o3",
            "💎 o3",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "CodexOAuth"),
    };
}
