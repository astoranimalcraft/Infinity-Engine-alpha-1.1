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

## Desktop integration kit (DIK)

`Prowl.DIK` is the user-mode desktop integration layer. Keep it read-only where the API name implies
inspection, and never write to kernel memory, drivers or real boot configuration:

- `DllTools` lists loaded modules and checks PE headers without loading anything new.
- `SecurityScanner` + `GameSecurityGuard` run before the player window opens. The generated
  `player.manifest` turns the check on by default; `--skip-security` bypasses it for trusted builds.
  Only a critical finding (bad PE, dangerous script command, incomplete install) blocks, and the
  player must click the affirmative button twice. Registry startup entries are only reported when
  they point at this game, so unrelated Windows startup entries cannot block a game.
- `HackSimulation` and `BsodSimulator` are timed visual effects. They never touch disks, the registry
  or the kernel; the text they show is real machine facts only.
- `DialogTools.Show` / `Dik.MessageBox` accept `requiredClicks`; `ConfirmWarning` defaults to two.
- Script compilation adds every `Prowl.*.dll` beside the editor, including `Prowl.DIK`, so game
  scripts can `using Infinity.DIK;`. A missing reference here makes the whole script assembly fail
  and every script disappears from the Add Component list.

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

## Authoring graphs

- `ScriptGraph` compiles C# nodes through `ScriptGraphCompiler` (Roslyn), then `ScriptGraphRunner`
  hosts the generated behaviour. `ScriptGraphApiCatalog` is the searchable API list the node editor
  shows, so a new runtime API only appears after it is added there.
- `MaterialGraph` compiles to a real `.shader` through `MaterialGraphCompiler`; `PostProcess` mode
  emits a Graphite fragment instead. Both graph windows live under `Prowl.Editor/GUI/NodeGraphs`.
- Timeline animations are `AnimaTrack` assets. `AnimationPlayer` is displayed as **Animation
  Source**; its `AnimaTrack` property is what the timeline assigns. `AnimaTrackNode` is the graph
  source, so timeline curves and animation graphs share the same `AnimationTake` curves.

## Test status

- `Prowl.Runtime.Test` passes except the environment-only `HeadlessRunTests` case (needs the
  headless run script outside this source tree) and the occasionally timing-sensitive
  `AnimatorRagdollTests` case, which passes when rerun alone.
- `Prowl.Editor.Test` has 65 focused tests that pass. Build/packaging tests fail because the desktop
  player staging and packaging projects are not part of this source tree.

## Script templates must compile

Every `Prowl.Editor/Resources/*.cstemplate` is compiled by user projects, so an API that changed in
the runtime silently breaks the scripts the New Script dialog creates. After touching a template,
expand `{[className]}` and build the results against `Prowl.Runtime` in a scratch project:

```bash
mkdir -p /tmp/tplcheck && cd /tmp/tplcheck
# csproj with a ProjectReference to Prowl.Runtime
for f in /tmp/infinity-src/Infinity-Engine-alpha-1.1/Prowl.Editor/Resources/*.cstemplate; do
  b=$(basename "$f" .cstemplate); sed 's/{\[className\]}/Tpl/g' "$f" > "$b.cs"; done
dotnet build
```

`AssetRef<T>` uses `Get()` / `LoadImmediate()` / `LoadAsync()`, not the old `Res` property, and
`SceneInstance.Add`/`Remove` are internal: spawn with `GameObject.Instantiate(prefab, Scene)` and
destroy with `GameObject.Destroy()`.

## Packaging must keep `Library` folders

Graphite compiles its built-in shaders from embedded resources under `Anthology/Graphite/Rendering/Library`
(`*.shader`, `*.glsl`, `Data/*.bin`). Excluding directories named `Library` while zipping removes those
resources, and the editor then crashes at startup with `Expected 'Shader' but found 'end of input'` because
`ShaderLibrary.Get` returns null. Only exclude the root `Build` output and `bin`/`obj`; never exclude
`Library` by name.
