// <copyright file="CodexOAuthTextModelDefaults.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

using Echoglossian.Translators.OpenAI;

namespace Echoglossian.Translators.CodexOAuth;

public static class CodexOAuthTextModelDefaults
{
    public const string DefaultModelId = "gpt-5";

    public static readonly List<LlmTextModel> PredefinedModels = new()
    {
        new LlmTextModel(
            "gpt-5",
            "⭐ GPT-5 [default]",
            true,
            false,
            true,
            false,
            IsDefault: true,
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
            "gpt-5.1-codex",
            "🤖 GPT-5.1 Codex",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "CodexOAuth"),
        new LlmTextModel(
            "codex-mini-latest",
            "💨 Codex Mini (latest)",
            true,
            false,
            true,
            true,
            IsDefault: false,
            "CodexOAuth"),
        new LlmTextModel(
            "gpt-4.1",
            "🔷 GPT-4.1",
            true,
            false,
            true,
            false,
            IsDefault: false,
            "CodexOAuth"),
        new LlmTextModel(
            "o4-mini",
            "🔶 o4-mini",
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
