// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Infinity.Graphite.Rendering;

namespace Infinity.Runtime.Rendering.Pipelines.Infinity;

/// <summary>
/// The shader axis that switches the standard surfaces between the Prowl Legacy look and the Infinity Modern look.
/// A view carries <see cref="Modern"/> while the modern pipeline is active, so every Lit, Sprite, Terrain and
/// Foliage material compiles and draws its modern variant without needing a second material or shader asset.
/// </summary>
public static class InfinityPipelineKeywords
{
    /// <summary>Declared by the standard shaders as <c>#pragma variant global INFINITY_PIPELINE LEGACY MODERN</c>.</summary>
    public static readonly GlobalAxis Pipeline = GlobalAxis.Define("INFINITY_PIPELINE", "LEGACY", "MODERN");

    public static GlobalKeyword Modern => Pipeline["MODERN"];

    /// <summary>
    /// Adds the modern keyword to a view's own keywords, or returns them unchanged while the legacy pipeline is
    /// selected. A caller's keywords, such as a debug view, are preserved.
    /// </summary>
    public static GlobalKeyword[]? Combine(GlobalKeyword[]? keywords)
    {
        if (!InfinityPipelineMode.Enabled)
            return keywords;

        GlobalKeyword modern = Modern;
        if (keywords != null)
        {
            foreach (GlobalKeyword keyword in keywords)
                if (keyword == modern)
                    return keywords;

            var combined = new GlobalKeyword[keywords.Length + 1];
            Array.Copy(keywords, combined, keywords.Length);
            combined[^1] = modern;
            return combined;
        }

        return [modern];
    }
}
