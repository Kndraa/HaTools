# CLAUDE.md — HaTools

Guide for Claude and other agents (and humans) working on this repository. Read this before adding or changing anything. It holds the project philosophy, conventions, full tool documentation and running notes.

## What this project is

**HaTools** (`com.kndra.hatools`) is a lightweight, editor-only Unity package of small tools for VRChat avatar creation. Every tool appears in Unity under **Tools > HaTools**.

Purpose: let creators test and optimise avatars inside Unity without launching VRChat, and generally speed up the avatar workflow. A good tool either answers "how will this look or perform in VRChat?" from the editor, or removes a repetitive manual step.

- Target: Unity 2022.3, Built-in Render Pipeline (the VRChat setup).
- Editor-only: nothing here ships in avatar uploads or runs in-game.
- Tools should be quick to run. Avoid long blocking operations; if one is unavoidable, show a progress bar and let the user cancel.

## Philosophy

### 1. Modularity

Adding or changing a tool must not require editing files that belong to other tools.

- One tool = one file in `Editor/Tools/`. If a tool genuinely needs several files, give it its own subfolder: `Editor/Tools/<ToolName>/`.
- Tools do not depend on each other. Shared code goes in `Editor/Core/` only when at least two tools need it, and changes to Core must stay backwards compatible.
- A pull request for one tool should touch only that tool's file(s), its section in this document, and its line in `README.md`.

All scripts compile into one assembly (`HaTools.Editor`), so splitting code across files has no meaningful cost to compile time.

### 2. Simplicity

- A script is only as long as it needs to be. Prefer plain, readable code over clever abstractions.
- No settings windows, frameworks or configuration systems unless a tool truly needs them.
- Only depend on Unity's own editor APIs. Add a dependency (for example the VRChat SDK) only when a tool can't work without it, and note why here.

### 3. Comments: short in code, full here

- Code comments are brief: what a block does, or why it's done that way.
- Each tool file starts with a one-line header that points here:
  `// <Tool name>: <one-line summary>. Full docs: CLAUDE.md > Tools > <Tool name>.`
- The full explanation (purpose, how it works, settings, caveats, known issues) goes in this file under **Tools**.

## Repository layout

```
package.json                 VPM/UPM package manifest (name, version, description)
README.md                    Short user-facing overview
CLAUDE.md                    This file
LICENSE.md                   MIT
Editor/
  HaTools.Editor.asmdef      Editor-only assembly for all scripts
  Core/
    HaToolsMenu.cs           Shared menu root constant
    AssemblyInfo.cs          Lets the tests see internal members
  Tools/                     <- all tools live here, one file (or subfolder) each
Tests/Editor/
  HaTools.Editor.Tests.asmdef Test assembly (only compiled when the package is "testable")
  CoreTests.cs               Checks shared conventions (menu root, namespace)
  <ToolName>Tests.cs         One test file per tool
.github/
  workflows/tests.yml        CI: compiles the package in Unity and runs the tests
  workflows/release.yml      Publishes a .unitypackage when a version tag is pushed
  scripts/build_unitypackage.py  Builds the .unitypackage (no Unity needed)
  test-project/              Throwaway Unity project the CI installs the package into
```

## Conventions

- **Namespace:** `HaTools`
- **Class names:** one class per tool, named after the tool (e.g. `LightingTestScene`): an `EditorWindow` when the tool has a window, a static class otherwise.
- **Menu path:** always build it from the shared constant, never type the root by hand:
  `[MenuItem(HaToolsMenu.Root + "Tool Name")]` or `[MenuItem(HaToolsMenu.Root + "Tool Name/Action")]`.
  Changing `HaToolsMenu.Root` renames the menu for every tool at once.
- **Undo:** any tool that changes scene objects or assets registers Undo (`Undo.RecordObject`, `Undo.RegisterCreatedObjectUndo`, ...).
- **Safety:** never modify the user's assets without asking first; write generated assets to a clearly named folder (e.g. `Assets/HaTools/<ToolName>/`).
- **Unity .meta files:** commit a `.meta` file for every file and folder. Unity creates them automatically when the package is opened in a project; commit those generated files. Never copy a `.meta` from another file (duplicate GUIDs break references).

## Adding a tool (checklist)

1. Create `Editor/Tools/<ToolName>.cs` (or a `<ToolName>/` subfolder).
2. Add the one-line header comment pointing to this file.
3. Use `HaToolsMenu.Root` for the menu path.
4. Add a section for the tool under **Tools** below.
5. Add one line for the tool under **Tools** in `README.md`.
6. Where practical, add `Tests/Editor/<ToolName>Tests.cs` (see Testing).
7. Open the package in Unity once so `.meta` files are generated, and commit them.
8. Don't bump `version` in `package.json`: it only changes when releasing (see Versioning).

## Testing

Every branch push (except pushes that only change Markdown or the licence) runs `.github/workflows/tests.yml` on GitHub Actions. It has two jobs:

- **Build .unitypackage** (seconds, no Unity): runs the release build script (see Releasing), which fails if any file or folder is missing its `.meta`. The result is uploaded as the `unitypackage` artifact (kept 14 days), so any commit can be downloaded from its run page and imported into Unity for testing. GitHub wraps artifacts in a `.zip`; unzip it to get the `.unitypackage`.
- **Edit Mode tests** (several minutes, needs the Unity licence secrets):
  1. It copies `.github/test-project/` to a throwaway Unity project, copies this package into that project's `Packages/com.kndra.hatools/` (an embedded package), and marks it testable in `Packages/manifest.json`.
  2. [GameCI's unity-test-runner](https://game-ci.com/docs/github/test-runner) opens the project in a headless Unity editor, compiles everything and runs the Edit Mode tests. Results appear as the **Edit Mode test results** check on the commit and as a `test-results` artifact.
  3. It fails if Unity had to generate any `.meta` file that isn't committed, and prints the generated files so they can be committed as they are.

**Unity version:** `.github/test-project/ProjectSettings/ProjectVersion.txt` (2022.3.22f1, the VRChat version). Change it there when VRChat moves to a new version.

**One-time setup (repository owner):** the runner needs a Unity licence. A free Personal licence works:

1. Sign in to Unity Hub on your own computer so it activates a Personal licence.
2. Find the licence file: Windows `C:\ProgramData\Unity\Unity_lic.ulf`, macOS `/Library/Application Support/Unity/Unity_lic.ulf`, Linux `~/.local/share/unity3d/Unity/Unity_lic.ulf`.
3. On GitHub, go to the repository's **Settings > Secrets and variables > Actions** and add three repository secrets: `UNITY_LICENSE` (the whole contents of that file), `UNITY_EMAIL` and `UNITY_PASSWORD` (your Unity account). Never paste these into a chat or commit them.

Until the secrets exist, every run fails straight away at the "Check Unity license secrets" step.

**Writing tests:**

- One file per tool: `Tests/Editor/<ToolName>Tests.cs`, namespace `HaTools.Tests`, using NUnit (`[Test]`).
- Tests can't click dialogs. Keep dialogs in the thin menu method and put the real work in `internal` methods that the tests call (`AssemblyInfo.cs` makes `internal` members visible to the test assembly).
- Clean up anything a test creates on disk (for example `Assets/HaTools/<ToolName>/`) in a `[TearDown]`.
- The tests are only compiled when the package is listed under `testables` (the CI project does this), so they never reach users' projects.
- Runs take several minutes and use the repository's GitHub Actions minutes. Pushes to the same branch cancel the previous run.

## Releasing

Releases are a single `.unitypackage` attached to a GitHub Release. It contains only `package.json` and the scripts: everything under `Editor/` (the tools, the shared menu code and the assembly definition). No README, licence, tests or docs. It installs into `Packages/com.kndra.hatools/`, so Unity treats it as a real package and Package Manager shows its name and version.

1. Bump `version` in `package.json` (see Versioning) in a commit of its own and merge it to `main`. Wait for the tests to pass.
2. Tag that commit with the same version and push the tag: `git tag v0.2.0 && git push origin v0.2.0`.
3. `.github/workflows/release.yml` checks the tag matches `package.json`, builds `HaTools-<version>.unitypackage` and creates the GitHub Release with generated notes.

`.github/scripts/build_unitypackage.py` builds the package without Unity. It takes `package.json` and the git-tracked files under `Editor/` (except `Editor/Core/AssemblyInfo.cs`, which only the tests need, and hidden files such as `.gitkeep`) and pairs each file and folder with its committed `.meta`. The same commit always gives a byte-identical file. Run it locally with `python3 .github/scripts/build_unitypackage.py`.

While the repository is private, only people with access to it can download releases. Importing a newer `.unitypackage` updates the files in place, but files removed from the package stay behind; delete `Packages/com.kndra.hatools` before importing if a release removed or renamed files.

## Versioning

Semantic versioning in `package.json`. The version is bumped only when releasing, never in a tool or feature branch, so parallel branches don't all edit the same line. Pick the bump from everything merged since the last release:

- Patch (`0.1.x`): bug fixes.
- Minor (`0.x.0`): new tools or new features in a tool.
- Major (`x.0.0`): breaking changes, such as removing a tool or changing the menu root.

## Tools

Full documentation for each tool. One `###` section per tool, in alphabetical order.

### Lighting Test Scene

- **File:** `Editor/Tools/LightingTestScene.cs`
- **Tests:** `Tests/Editor/LightingTestSceneTests.cs`. `LightingTestSceneTests`: scene contents, station lights, moving with Undo and scene safety. `LightingTestSceneSwitchTests`: entering the test scene (the copy, the other scenes closed, rebuilding over assets left by an earlier build), returning (every scene reopened, the original avatar found, the copy not saved, deleted scenes skipped, nothing remembered) and deleting. `LightingTestSceneBakeTests`: bakes the scene (CPU lightmapper, since CI has no GPU) and reads each station's light probes to check the stations really differ: baked lamps light A, E and F, realtime lamps B and C stay out of the probes, A is warm and E neutral, F is red on one side and blue on the other; it also checks the bake is still there after switching to the main scene and back. Takes longer than the other tests.
- **Menu:** Tools > HaTools > Lighting Test Scene (opens a window)
- **Purpose:** see how an avatar's shaders (lilToon, Poiyomi, ...) react to the kinds of world lighting found in VRChat, without uploading or launching VRChat.
- **How it works:**
  1. Pick the avatar in the scene in the window's *Avatar* field (it starts with the current selection). *Create Test Scene and Bake* offers to save the open scenes, builds the test scene at `Assets/HaTools/LightingTestScene/LightingTest.unity`, puts a copy of the avatar in it, closes your own scenes and starts an async bake. The scene has six stations 15 m apart on the X axis:
     - A: baked warm point lamp. The avatar only receives it through light probes.
     - B: realtime warm lamp, render mode Not Important (vertex light).
     - C: realtime warm lamp, render mode Important (pixel light).
     - D: no lamp, only the dim flat ambient of a dark world.
     - E: baked neutral white lamp, as a colour reference for A.
     - F: baked red lamp on the avatar's left (-X) and blue lamp on its right (+X). The probes then hold light that changes with direction. A shader that shades by direction shows a red side and a blue side; one that flattens probe light into a single colour (lilToon averages it and works out one light direction) shows a mix.
     Each station has a static floor and back wall, a grid of 125 light probes, a label and a dynamic grey reference sphere that is lit the same way an avatar is. There is no skybox, and an optional realtime sun is included but disabled (turning it on lights every station). The tool writes its materials and a fast, low-resolution `LightingTestSettings.lighting` asset (Progressive GPU) into the same folder.
  2. While the test scene is open the window shows one button per station. A click moves the object in the *Avatar* field (the copy) to that station, facing +Z, with Undo, and frames the Scene view on its face; clicking the current station again brings both back. Under the buttons is a short note on what to look for at that station. Above them is the bake status, with a *Bake* button when the scene has no bake.
  3. *Return to Main Scene* removes the copy, saves the test scene with its bake, reopens the scenes that were open before (loaded and unloaded ones, same active scene), puts the Scene view camera back and fills the *Avatar* field with the original avatar again.
  4. Back in your own scene, *Open Test Scene* switches to the kept scene with a fresh copy of the avatar and no new bake, and *Delete Test Scene* deletes `Assets/HaTools/LightingTestScene/` (asks first).
- **Play mode:** the window says the test is more accurate to VRChat in Play mode. The lighting itself is the same, but the copy keeps its scripts, so build tools (Modular Avatar, VRCFury, ...) process it as they do at upload, and Gesture Manager or Av3 Emulator can run its toggles. The station buttons stay enabled in Play mode; opening, returning, deleting and baking don't (Unity can't switch or save scenes while playing). The scene has no camera, so use the Scene view. Not tried yet with Modular Avatar or VRCFury installed: if a tool replaces the copy with a new object in Play mode, drop that object into the *Avatar* field so the station buttons move it.
- **Settings / options:** none. Edit the lamp colours, intensities and positions in the code if needed.
- **Caveats / known issues:**
  - The test runs on a copy. Material edits stay (materials are assets); changes to the copy's objects and components are thrown away on return, and anything on the avatar that points at an object elsewhere in your scene is empty on the copy.
  - Your scenes must be saved to a file, since an untitled scene can't be reopened. Choosing *Don't Save* in Unity's save prompt discards those unsaved changes, because the scenes are closed.
  - What to return to (scene list, camera, avatar) is kept in the project's user settings (`EditorUserSettings`, key `HaTools.LightingTestScene.Return`), so it survives closing the window. If the test scene is opened by hand there is nothing remembered, and returning opens a new empty scene. If you leave the test scene by hand, *Open Test Scene* and *Delete Test Scene* still work from your own scene.
  - Returning while the bake is still running cancels it; the next *Open Test Scene* bakes again because the scene has no bake yet.
  - Saving the test scene by hand (Ctrl+S) saves the copy in it. The next *Open Test Scene* removes old copies (root objects whose name ends in "(Lighting Test Copy)") before adding the new one.
  - Deleting removes the whole `Assets/HaTools/LightingTestScene/` folder, and `Assets/HaTools/` too if nothing else is in it.
  - Station buttons only move objects that are inside the test scene.
  - The Progressive GPU lightmapper falls back to CPU (slower) on unsupported GPUs.
  - Earlier versions wrote to `Assets/LightingTestScene/` (standalone draft) or `Assets/Kndra tools/LightingTestScene/` (before the rename to HaTools). Those folders can be deleted.
- **Reading the results:** some differences between shaders are their default settings, not bugs. lilToon defaults checked against its shader source (Lighting section of the material):
  - B: lilToon ignores vertex lights by default (Vertex Light Strength 0), so it looks like D at station B. Standard and other shaders are lit.
  - D: in a Linear colour space project (as VRChat uses) the ambient colour is about 0.005 in linear terms, below lilToon's Light Min Limit (0.05), so lilToon renders brighter than Standard there. This shows each shader's minimum brightness.
  - Bright lamps: lilToon caps light at Light Max Limit (1); Standard does not.
  - Shadows and reflections aren't tested: no lamp casts shadows (lilToon also ignores cast shadows by default, Receive Shadow 0), and with no skybox or reflection probe every reflection and environment-based effect sees black.
  - Comparing materials side by side: a renderer without an Anchor Override samples probes at its own bounds centre, and pixel/vertex light depends on distance to the lamp. Give the objects being compared the same Anchor Override and the same distance to the lamp, or the difference you see is partly position, not shader.

### Root Bone and Anchor Fixer

- **File:** `Editor/Tools/RootBoneAndAnchorFixer.cs`
- **Tests:** `Tests/Editor/RootBoneAndAnchorFixerTests.cs`: the check (shared, different and missing root bones and anchors, particles ignored), the override (every renderer including disabled ones, Undo, empty fields left alone) and the bounds conversion when the root bone changes.
- **Menu:** Tools > HaTools > Root Bone and Anchor Fixer (opens a window)
- **Purpose:** give every renderer of an avatar the same root bone and the same light anchor (Anchor Override). Renderers that sample lighting from different points look lit differently in VRChat; mismatched root bones give skinned meshes bounds in different places.
- **How it works:**
  1. Pick the avatar in the scene in the *Avatar* field (it starts with the current selection).
  2. *Check Root Bones and Light Anchors* shows the result right below the button: either that every renderer shares one root bone / anchor, or a list of each value with the renderers using it. Root bones only exist on skinned mesh renderers; light anchors are checked on mesh and skinned mesh renderers. A missing root bone or anchor never counts as shared, since each renderer then uses its own transform or bounds centre.
  3. Reference the root bone and light anchor to use in the *Root Bone* and *Light Anchor* fields (both must be inside the avatar), then *Override All Renderers*. It sets them on every mesh and skinned mesh renderer, including ones on disabled objects, with Undo, and shows the check again. An empty field leaves that setting as it is.
- **Settings / options:** none besides the two fields.
- **Caveats / known issues:**
  - Skinned mesh bounds are stored relative to the root bone, so when the root bone changes the bounds are converted to cover the same space as before (the box around the old box, which can be a little larger if the bones are rotated differently). Bounds that were already too small stay too small.
  - Particle, trail and line renderers are left alone.
  - Tools that change renderers when the avatar is built (for example Modular Avatar's Mesh Settings) can override these values in the uploaded avatar.

### Shader Fallback Preview

- **File:** `Editor/Tools/ShaderFallbackPreview.cs`
- **Tests:** `Tests/Editor/ShaderFallbackPreviewTests.cs`: the fallback rules for tags and shader names (plain C#, no engine calls), the fallback materials (shader, copied texture, Standard rendering mode, Hidden, combined notes), and the preview copy (unsaved, beside the avatar, one fallback per material, no scripts, visible even under a disabled parent, clean removal that never destroys the avatar's own materials).
- **Menu:** Tools > HaTools > Shader Fallback Preview (opens a window)
- **Purpose:** see the avatar the way other players see it when they have its shaders blocked (VRChat's Safety settings), without launching VRChat.
- **How it works:**
  1. Pick the avatar in the scene in the window's *Avatar* field (it starts with the current selection).
  2. *Create Fallback Preview* copies the avatar next to the original (to its left, one avatar width plus 0.5 m away), swaps every material on the copy for its fallback, and points the Scene view camera straight at the copy's front (avatars face +Z), showing all of it. The window lists each material with its shader, the fallback VRChat picks, the Unity shader used to show it, and any caveat.
  3. *Remove Preview and Restore Camera* deletes the copy and its fallback materials and puts the Scene view camera back where it was before the first preview. Closing the window, entering Play mode or closing the scene removes the preview too. *Recreate* rebuilds the copy (for example after changing a material) and keeps the saved camera.
- **Fallback rules** (from VRChat's "Shader Blocking and Fallback System" page, `vrchat-community/creator-docs`, `Docs/docs/avatars/shader-fallback-system.md`):
  - *With a `VRCFallback` tag* (read with `Material.GetTag`, so a per-material override tag counts too): the tag picks the type (`Unlit`, `VertexLit`, `Toon`, `MobileToon`, `Particle`, `Sprite`, `Matcap`, otherwise Standard) and the mode (`Cutout`, `Fade`, `Transparent`, plus `DoubleSided` for Toon). `Hidden` hides the mesh. `toonstandard` and `toonstandardoutline` use `VRChat/Mobile/Toon Standard (Outline)` with every same-named property and keyword. The Standard shader properties (`_MainTex`, `_Color`, `_BumpMap`, `_EmissionMap`, ...) are copied.
  - *Without a tag:* a material using one of the shaders VRChat has built in (Standard, the Legacy shaders, `Sprites/Default`, `Toon/Lit`, ...) keeps it unchanged. Otherwise the shader name is searched (case-sensitive) for `Sprite`, `Particle`, `MatCap`, `Toon`, `Unlit`, `VertexLit` (type, first match wins) and `Cutout`, `Fade`, `Transparent` (mode); a `_Ramp` property means Toon, and the `_ALPHATEST_ON` / `_ALPHABLEND_ON` keywords mean Cutout / Transparent. Only `_MainTex` and `_Color` are copied (plus `_Ramp` and `_MatCap`).
  - Toon with Transparent or Fade becomes Unlit Transparent: there is no transparent Toon fallback.
- **Preview shaders:** Unlit, VertexLit, Sprite and Standard use the matching Unity built-in shaders (Standard gets the same blend, depth and keyword settings as its inspector sets for the mode). MobileToon, Matcap and Toon Standard use the VRChat SDK's `VRChat/Mobile/...` shaders; without the SDK the preview shows Standard and says so. Hidden uses an invisible Standard cutout material.
- **Settings / options:** none.
- **Caveats / known issues:**
  - Approximations: VRChat's own Toon fallback isn't available in Unity, so Toon uses `VRChat/Mobile/Toon Lit` (Cutout: `Legacy Shaders/Transparent/Cutout/Diffuse`) and double-sided faces and outlines aren't shown. Particle uses `Legacy Shaders/Particles/Alpha Blended`. A shader without `_MainTex` and `_Color` shows as Standard, while VRChat shows a matcap in the viewer's trust rank colour. The docs don't say which type wins when a name matches several words, so the order above is a guess.
  - The docs were last updated in 2022 and VRChat says the system may change. Check the result in VRChat with the Action Menu's *Options > Avatar > Fallback Shaders* toggle when it matters.
  - Keywords (normal map, emission) aren't turned on in fallback materials, since new materials start without them; whether VRChat enables them isn't documented.
  - The copy has no scripts (VRChat SDK components, PhysBones, Modular Avatar, ...), so the SDK and other tools don't treat it as a second avatar. Its pose is the avatar's current pose.
  - The copy sits under the avatar's parent, unless that parent is disabled: then it goes to the scene root (same world position and size) so it can be seen.
  - The copy and its materials are `DontSave`, so they never end up in the scene file, and no Undo is recorded for them (removing the preview is the undo). Creating it still marks the scene as modified.
  - The camera is the last active Scene view. Without one open, the copy is still made but the camera isn't moved.

<!--
Template:

### <Tool name>

- **File:** `Editor/Tools/<ToolName>.cs`
- **Menu:** Tools > HaTools > <Tool name>
- **Purpose:** what problem it solves.
- **How it works:** step by step.
- **Settings / options:** if any.
- **Caveats / known issues:**
-->

## Notes

Running notes: decisions, ideas and things to remember. Newest first, each dated.

- 2026-10-02: Lighting Test Scene revised into a window: avatar field, one button per station, and buttons to create, open, return from and delete the test scene. The avatar is copied into the test scene instead of being dragged in by hand, and the scenes that were open, the camera and the avatar are restored on return. The test scene is kept between sessions so it isn't baked every time. The renderer check was removed (Root Bone and Anchor Fixer covers light anchors). Considered and dropped: building the stage in the user's own scene (a real bake there replaces that scene's lighting data, and faking the probes means overriding the scene's own lights and environment), and a viewport inside the window (Unity can't bake a window's private scene).
- 2026-10-02: Unity 2022.3.22f1 is installed on the owner's machine (`D:\Unity\Unity Editor\2022.3.22f1`), so local sessions can run the Edit Mode tests without CI: copy `.github/test-project/` to a temporary folder, add `Assets/` and the package under `Packages/com.kndra.hatools/` (as the workflow does), then `Unity.exe -batchmode -projectPath <copy> -runTests -testPlatform EditMode -testResults <file>`.
- 2026-09-28: Added Root Bone and Anchor Fixer (the "Anchor Override fixer" idea, widened to root bones). Built as its own tool, not a button in Lighting Test Scene's renderer check, since that tool is due for a full revision. The override converts skinned mesh bounds to the new root bone so they don't move.
- 2026-09-28: Versions are bumped only when releasing, not in each tool branch: every branch bumping `package.json` made parallel branches conflict on the same line.
- 2026-09-28: Added Shader Fallback Preview: a window that makes a temporary copy of the avatar with VRChat's fallback shaders beside the original and frames it in the Scene view; removing it restores the camera. Fallback rules follow VRChat's docs page (their site is blocked from the cloud sessions, the docs repo `vrchat-community/creator-docs` is not). The copy is placed beside the avatar rather than hiding the original, so the avatar itself is never touched.
- 2026-09-28: Compile check without Unity (cloud sessions): Unity 2022.3.22f1's compiler, .NET runtime and reference DLLs can be taken from GameCI's `unityci/editor:ubuntu-2022.3.22f1-base-3` image through `mirror.gcr.io` (Unity's own download servers are blocked; Docker Hub rate-limits). Only the needed files are extracted from the 3.7 GB layer (about 180 MB), outside the repo, and never committed (Unity licence). Tests that don't call the engine (pure C# logic) can also run with NUnit's .NET Standard build; the rest need the CI.
- 2026-09-27: Renamed the project from "Kndra tools" to HaTools (0.3.0): package id `com.kndra.hatools`, namespace `HaTools`, assemblies `HaTools.Editor` / `HaTools.Editor.Tests`, menu `Tools/HaTools/`, shared class `HaToolsMenu`, output folder `Assets/HaTools/`, release file `HaTools-<version>.unitypackage`. A minor bump rather than major because nothing had been released yet. Projects with the old package must delete `Packages/com.kndra.tools` before installing.
- 2026-09-27: Tool ideas reviewed. Dropped a texture memory report and a performance stats estimate (avatar projects always load the VRChat SDK, which already reports both) and a missing reference finder. The ideas kept are listed at the end of these notes.
- 2026-09-27: Lighting Test Scene review: added station F (red/blue split lighting), a bake test that checks each station's probes, tighter unit tests, and notes on reading results with lilToon. Ideas not done yet: stations for overbright light, two overlapping pixel lights (lilToon's add pass blends with Max by default, so they don't add up), a lamp behind/below the avatar, realtime shadows and a reflection probe; a contact sheet that renders every station per material into one image.
- 2026-09-27: Added Lighting Test Scene (0.2.0), ported from a standalone script. Changes from the draft: Kndra menu, namespace and output folder; moving is limited to the test scene; the anchor check counts renderers without an Anchor Override as separate sample points; a running bake is cancelled before rebuilding. Build and check logic split into internal methods (`BuildScene`, `MoveToStation`, `AnalyseRenderers`) so they can be tested without dialogs.
- 2026-09-27: Added releases: pushing a `v*` tag publishes a `.unitypackage` (built by a script, no Unity) holding only `package.json` and the `Editor/` scripts, installed into `Packages/com.kndra.tools/`. Importing it into a real project hasn't been tried yet.
- 2026-09-27: Added CI (GameCI, Edit Mode tests, missing-.meta check) and Core convention tests. Needs the Unity licence secrets described under Testing.
- 2026-09-27: Purpose clarified: test and optimise avatars without running VRChat, plus general workflow improvements.
- 2026-09-27: Repository created. Package id `com.kndra.tools`, display name "Kndra tools", menu `Tools/Kndra tools/`. No tools yet.
- Planned: more Lighting Test Scene stations: overbright light, two overlapping pixel lights, a lamp behind or below the avatar, a reflection probe.
- Idea: Bounds check. Skinned mesh bounds that are too small make parts of the avatar disappear at the edge of the view. Approach still to be discussed: simply setting one shared bounds and root bone on every renderer was judged a little redundant.
- Idea: Lighting Test Scene contact sheet. Render every station for each material into one image grid (rows: materials, columns: stations A-F) to compare at a glance. To be discussed.
- Idea: Material comparison tool. Any two materials side by side, with the differences highlighted. Layout to be described later.
