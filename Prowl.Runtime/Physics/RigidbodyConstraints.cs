// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Infinity.Runtime;

/// <summary>
/// World-space axes a <see cref="Rigidbody3D"/> is not allowed to move or turn along. Useful for
/// keeping a character upright, or pinning a body to a plane.
/// </summary>
[Flags]
public enum RigidbodyConstraints
{
    None = 0,

    FreezePositionX = 1 << 0,
    FreezePositionY = 1 << 1,
    FreezePositionZ = 1 << 2,

    FreezeRotationX = 1 << 3,
    FreezeRotationY = 1 << 4,
    FreezeRotationZ = 1 << 5,

    FreezePosition = FreezePositionX | FreezePositionY | FreezePositionZ,
    FreezeRotation = FreezeRotationX | FreezeRotationY | FreezeRotationZ,
    FreezeAll = FreezePosition | FreezeRotation
}
