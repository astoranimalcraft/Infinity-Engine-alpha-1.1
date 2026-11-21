// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text;

namespace Prowl.Runtime.Rendering.Pipelines;

public sealed class RenderPipelineSettings
{
    public RenderPipelineType Pipeline = RenderPipelineType.Legacy;

    public bool InfinityEnabled = true;

    public int version = 1;
}
