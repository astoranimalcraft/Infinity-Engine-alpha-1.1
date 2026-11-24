// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Infinity.Runtime.UI;

/// <summary>
/// Determines how a <see cref="GameCanvas"/> computes the scale factor.
/// </summary>
public enum ScaleMode
{
    /// <summary>
    /// UI elements retain the same pixel size regardless of screen resolution.
    /// </summary>
    ConstantPixelSize,

    /// <summary>
    /// UI elements scale with the screen size based on a reference resolution.
    /// </summary>
    ScaleWithScreenSize,
}
