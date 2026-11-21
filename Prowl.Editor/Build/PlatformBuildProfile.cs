using System;

namespace Prowl.Editor.Build;

/// <summary>
/// The per-platform settings a build pipeline keeps. A pipeline that needs its own options declares a subclass and
/// returns its own type from <see cref="GetPipelineType"/>, which is what the settings file keys the profiles by.
/// </summary>
public abstract class PlatformBuildProfile
{
    /// <summary>The pipeline type this profile belongs to.</summary>
    public abstract Type GetPipelineType();

    /// <summary>Puts every field back to its default, which the settings panel's reset runs on each profile.</summary>
    public virtual void ToDefault() { }
}
