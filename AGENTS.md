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
- Script templates (`Prowl.Editor/Resources/*.cstemplate`) are compiled by the user's project, so their
  usings must be `Infinity.Runtime` / `Infinity.Runtime.Resources` / `Infinity.Vector`. A stale
  `using Prowl.Runtime;` makes every new script fail to build, which also breaks drag and drop: the
  component type never exists, so dropping a script on an object silently does nothing.

## Origami draw callbacks run without the theme

`EditorApplication.EndGui` pops the editor's Origami theme (`_origamiScope.Dispose()`) before Paper
flushes the frame's draw callbacks, so `Origami.Current` inside a `paper.Draw(...)` callback is the
empty root theme: no fonts, no colours. Resolve theme values while building widgets (as
`CodeEditor.Build` does for its font), never inside the draw callback. A draw-time
`Origami.Current.Mono` lookup is what made the script editor render nothing but its background.

## Infinity post effects

The Infinity family lives in `Prowl.Runtime/Rendering/Pipelines/Infinity`. A one pass effect is a
`PostEffect` subclass: one `VolumeParameter` field per uniform (the field name with `_` in front), the
GLSL in the `Shader` string, `Graphite_SampleSource(vUV)` in and `outColor` out. Pick the stage with
`Stage` (`AfterTonemap` for display space looks, `BeforeTonemap` for anything that must blur or glow in
linear HDR) and set `ReadsDepth` to get `_CameraDepthTexture`, reversed: 1 near, 0 far. `_Time` is
bound automatically, in seconds, so animated shaders need no parameter for the clock. Gate `IsActive`
on `InfinityPipelineMode.Enabled` so the stock Prowl components take over when the mode is off.

Verify a new effect by adding it to a profile on the scene camera through the CLI
(`VolumeProfile.CreateCameraDefault`, `profile.Add<T>()`, `camera.Profile = profile`) and reading the
scene view; a broken shader logs a Graphite compile error and leaves the view unchanged.

## Headless editor checks

```bash
Xvfb :99 -screen 0 1400x900x24 &
export DISPLAY=:99 VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json
./Build/Editor/Release/net11.0/Prowl.Editor &   # needs `openbox` for mouse input, dotnet on PATH for script builds
import -window root /tmp/shot.png                # ImageMagick
```

## Application icon

`Window.SetWindowIcon` takes raw RGBA and is public. Without an embedded icon, the window looks for
`icon.png`/`icon.ico` beside the executable, which is why a build copies both there. `AppIcon.Load`
turns either format into the array the window wants (an `.ico` yields every size, so `DecodeAllFrames`
must stay on). `AppIconExporter.TryExport` writes `icon.png` plus a 16-256 `icon.ico`, and Project
Settings > General applies the choice to the editor window as a live preview.
