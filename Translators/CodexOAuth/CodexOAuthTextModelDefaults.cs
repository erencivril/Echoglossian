// <copyright file="CodexOAuthTextModelDefaults.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

using Echoglossian.Translators.OpenAI;

namespace Echoglossian.Translators.CodexOAuth;

public static class CodexOAuthTextModelDefaults
{
    public const string DefaultModelId = "gpt-5-codex";

    public static readonly List<LlmTextModel> PredefinedModels = new()
    {
        new LlmTextModel(
            "gpt-5-codex",
            "🤖 GPT-5 Codex",
            true,
            false,
            true,
            false,
            IsDefault: true,
            "CodexOAuth"),
        new LlmTextModel(
            "gpt-5",
            "⭐ GPT-5",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "CodexOAuth"),
        new LlmTextModel(
            "gpt-5-mini",
            "⚡ GPT-5 Mini",
            true,
            false,
            true,
            true,
            IsDefault: false,
            "CodexOAuth"),
        new LlmTextModel(
            "gpt-5.3-codex",
            "🔷 GPT-5.3 Codex",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "CodexOAuth"),
    };
}
