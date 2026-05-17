// <copyright file="CodexOAuthTextModelDefaults.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

using Echoglossian.Translators.OpenAI;

namespace Echoglossian.Translators.CodexOAuth;

public static class CodexOAuthTextModelDefaults
{
    public const string DefaultModelId = "gpt-5.5";

    public static readonly List<LlmTextModel> PredefinedModels = new()
    {
        new LlmTextModel(
            "gpt-5.5",
            "⭐ GPT-5.5 (current) [default]",
            true,
            false,
            true,
            false,
            IsDefault: true,
            "CodexOAuth"),
        new LlmTextModel(
            "gpt-5.4",
            "🤖 GPT-5.4",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "CodexOAuth"),
        new LlmTextModel(
            "gpt-5.4-mini",
            "⚡ GPT-5.4 Mini",
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
        new LlmTextModel(
            "gpt-5.2",
            "💎 GPT-5.2",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "CodexOAuth"),
    };
}
