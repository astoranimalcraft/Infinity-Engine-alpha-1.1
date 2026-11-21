# Notes for agents working in this repository

## Build

The engine builds against a local Anthology checkout, not NuGet packages. `prowl.local.props` in the
repository root turns that on, and it is gitignored, so a fresh clone has to create it:

```xml
<Project>
  <PropertyGroup>
    <ProwlLocalAnthology>true</ProwlLocalAnthology>
  </PropertyGroup>
</Project>
```

`AnthologyDir` defaults to `../Anthology/` and expects `Graphite/Graphite/Prowl.Graphite.csproj` there.
Without it the build resolves older `Prowl.*` packages and fails on types such as `RenderView`.

```bash
dotnet build Prowl.slnx -c Release
dotnet build Prowl.Editor/Prowl.Editor.csproj -c Release
dotnet test Prowl.Runtime.Test/Prowl.Runtime.Test.csproj -c Release
```

## Test environment

- The container has no ICU, so `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` is set globally.
  `HeadlessRunTests.RunHeadless_GivesThreadsStartedWithoutItsContextTheInvariantCulture` cannot pass
  here because it constructs a `de-DE` culture; it is an environment limit, not a code defect.
- `AnimatorRagdollTests.ALimpRagdoll_ComesToRest_AndSleeps` fails in this container too; it is a
  physics settling test and does not touch the animation authoring code.

## Conventions

- Every file starts with the Infinity header (`// This file is part of the Infinity Engine`).
- Namespaces are `Infinity.*`; assembly and project names stay `Prowl.*` so build paths keep working.
- Analyzers reject null-conditional access on `EngineObject` (`?.`, and `??` on the result): use
  `IsValid()` / `IsNotValid()` first.
- Animation authoring lives in `Prowl.Runtime/Animation/Authoring` (take and tracks) and
  `Prowl.Runtime/Components/AnimationPlayer.cs`; the editor surface is
  `Prowl.Editor/GUI/Panels/AnimationTimelinePanel.cs` (Window/Animation/Timeline).

## Rebrand pitfalls (namespaces vs assembly names)

The rebrand renamed namespaces to `Infinity.*` but left assembly, project and resource names as
`Prowl.*`. Anything that hardcodes an assembly or manifest-resource name breaks at runtime and
usually fails silently:

- Embedded resources are `Prowl.Editor.Resources.<path>`. `EditorApplication.ResourceRoot` derives
  the prefix from `Assembly.GetManifestResourceNames()`; never hardcode it. A wrong prefix shows up
  as an editor window with no text (the bundled fonts fail to load) and as locale keys returned
  verbatim.
- The generated user-script `.csproj` must reference the loaded assemblies by
  `Assembly.GetName().Name` / `Assembly.Location` (`ScriptCompiler.GenerateCsproj`), not
  `Infinity.Runtime.dll`.
- Analyzer DLLs are matched by suffix (`.Analyzers` / `.Analyzer`), and the CLI executable name comes
  from `typeof(Infinity.Cli.CliProtocol).Assembly` (`CliServer.WriteLauncher`).
- Prefer `type.Assembly == typeof(...).Assembly` over comparing `GetName().Name` to `Infinity.*`.

## Headless editor checks

```bash
Xvfb :99 -screen 0 1400x900x24 &
export DISPLAY=:99 VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json
./Build/Editor/Release/net11.0/Prowl.Editor &   # needs `openbox` for mouse input, dotnet on PATH for script builds
import -window root /tmp/shot.png                # ImageMagick
```
