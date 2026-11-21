// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text;

namespace Infinity.Runtime.Rendering.Pipelines;

public sealed class RenderPipelineSettings
{
    public RenderPipelineType Pipeline = RenderPipelineType.Legacy;

    public bool InfinityEnabled = true;

    public int version = 1;
}
