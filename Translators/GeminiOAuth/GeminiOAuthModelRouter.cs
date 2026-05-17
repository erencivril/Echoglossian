// <copyright file="GeminiOAuthModelRouter.cs" company="lokinmodar">
// Copyright (c) lokinmodar. All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International Public License license.
// </copyright>

namespace Echoglossian.Translators.GeminiOAuth;

/// <summary>
///     Picks the cheapest Gemini variant capable of translating a given piece of game
///     text. Short NPC barks go to Flash Lite (fast + cheap); medium dialog goes to
///     Flash; long cutscene narration falls back to the user-selected model
///     (typically Pro) for the quality bump that long passages benefit from.
/// </summary>
internal static class GeminiOAuthModelRouter
{
    private const string FlashLite = "gemini-3.1-flash-lite-preview";
    private const string Flash = "gemini-3.1-flash-preview";

    private const int ShortTextMaxLength = 50;
    private const int MediumTextMaxLength = 250;

    /// <summary>
    ///     Returns the model that should service this translation.
    /// </summary>
    /// <param name="text">The source text about to be translated.</param>
    /// <param name="fallbackModel">User-selected model from the settings dropdown.</param>
    /// <param name="autoRouteEnabled">Master switch for auto-routing.</param>
    internal static string Route(string text, string fallbackModel, bool autoRouteEnabled)
    {
        if (!autoRouteEnabled || string.IsNullOrEmpty(text))
        {
            return fallbackModel;
        }

        return text.Length switch
        {
            < ShortTextMaxLength => FlashLite,
            < MediumTextMaxLength => Flash,
            _ => fallbackModel,
        };
    }
}
