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
  workflows/release.yml      Publishes a release and the VCC listing when a version tag is pushed
  scripts/build_unitypackage.py  Builds the .unitypackage and the VPM .zip (no Unity needed)
  scripts/build_vpm_listing.py   Builds the VCC listing and its "Add to VCC" page from the version tags
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

A release is a GitHub Release with two files: `HaTools-<version>.unitypackage` to import by hand, and `com.kndra.hatools-<version>.zip` for the VRChat Creator Companion (VCC). Both contain only `package.json` and the scripts: everything under `Editor/` (the tools, the shared menu code and the assembly definition). No README, licence, tests or docs. Both install into `Packages/com.kndra.hatools/`, so Unity treats it as a real package and Package Manager shows its name and version.

1. Bump `version` in `package.json` (see Versioning) in a commit of its own and merge it to `main`. Wait for the tests to pass.
2. Tag that commit with the same version and push the tag: `git tag v0.2.0 && git push origin v0.2.0`.
3. `.github/workflows/release.yml` checks the tag matches `package.json`, builds both files, creates the GitHub Release with generated notes and republishes the VCC listing.

`.github/scripts/build_unitypackage.py` builds the package without Unity. It takes `package.json` and the git-tracked files under `Editor/` (except `Editor/Core/AssemblyInfo.cs`, which only the tests need, and hidden files such as `.gitkeep`) and pairs each file and folder with its committed `.meta`. It writes the `.unitypackage` and the `.zip` (the same files under their own paths, with `package.json` at the root, which is the layout the VCC installs). The same commit always gives byte-identical files. Run it locally with `python3 .github/scripts/build_unitypackage.py`.

**VCC listing:** the VCC installs packages from a listing, a JSON file that names every version and where its `.zip` is. `.github/scripts/build_vpm_listing.py` builds it from the `v*` tags (each tag's `package.json` plus the address of that release's `.zip`), together with a small page whose only job is to open a `vcc://vpm/addRepo` link: GitHub removes `vcc://` links from a README, so the README links to that page instead. The release workflow pushes both to the `gh-pages` branch (one commit, replaced every release), which GitHub Pages serves at `https://kndraa.github.io/HaTools/` (`index.json` is the listing users can paste into the VCC under Settings > Packages > Add Repository). GitHub switched Pages on by itself when the `gh-pages` branch was first pushed (2026-10-03); the setting is under **Settings > Pages**, with `gh-pages` as the source. The tests workflow also runs the script, without publishing, so a broken script shows up before a release.

Importing a newer `.unitypackage` updates the files in place, but files removed from the package stay behind; delete `Packages/com.kndra.hatools` before importing if a release removed or renamed files.

## Versioning

Semantic versioning in `package.json`. The version is bumped only when releasing, never in a tool or feature branch, so parallel branches don't all edit the same line. Pick the bump from everything merged since the last release:

- Patch (`0.1.x`): bug fixes.
- Minor (`0.x.0`): new tools or new features in a tool.
- Major (`x.0.0`): breaking changes, such as removing a tool or changing the menu root.

## Tools

Full documentation for each tool. One `###` section per tool, in alphabetical order.

### Bounds Fixer

- **File:** `Editor/Tools/BoundsFixer.cs`
- **Tests:** `Tests/Editor/BoundsFixerTests.cs`, on a small humanoid built in code: the overshoot maths (worst side, in metres, root bone scale), every pose's muscle names, the check (tight bounds far from the root bone are too small, a mesh that follows the root bone or has room is fine, the worst pose is named, legs are moved, only the current pose without a humanoid rig, disabled renderers and renderers without a root bone are checked, renderers without a mesh are skipped, the avatar and scene are left alone, only the renderers that are too small start shown in the Scene view) and growing (fits every pose with a margin, Undo, never shrinks, fine bounds untouched, stored bounds with Update When Offscreen).
- **Menu:** Tools > HaTools > Bounds Fixer (opens a window)
- **Purpose:** find skinned meshes whose bounds are too small, and fix them. A skinned mesh stops being drawn when its bounds box leaves the view, even while the mesh itself is still in view, so parts of the avatar vanish at the edge of the screen. The box is fixed relative to the root bone, and a mesh always fits its box in the pose it was imported in, so nothing looks wrong in the editor: a glove whose box sits around the hands in T-pose leaves that box as soon as the arms come down.
- **How it works:**
  1. Pick the avatar in the scene in the *Avatar* field (it starts with the current selection).
  2. *Check Bounds* copies the avatar into a hidden preview scene, moves the copy through the test poses below with Unity's humanoid muscles (`HumanPoseHandler`), bakes every skinned mesh in each pose and measures the box around its vertices in the root bone's space. The copy sits under a disabled object, so none of its scripts run, and it is thrown away afterwards: the avatar and its scene are not touched.
  3. The window shows two foldouts, both closed: *Incorrect bounds* (too small) and *Correct bounds* (large enough), each with the number of skinned mesh renderers in it (ones on disabled objects included). Opening one lists its renderers; an incorrect one says how far the mesh sticks out of its bounds in metres and the pose where it sticks out the most, worst first. Clicking a renderer shows it in the Hierarchy.
  4. Bounds are drawn in the Scene view (Unity only draws the selected renderer's): red when too small, green when large enough. The checkbox in front of each renderer turns its box on or off. The checkbox on a foldout is on while any renderer under it is on, and clicking it turns them all on or off; an empty foldout has no checkbox. After a check the incorrect renderers are on and the correct ones are off, so the view only shows the problems. An incorrect renderer that is on also gets a yellow box showing what growing would set; turning the renderer off hides both. A renderer keeps its checkbox when it is grown: it moves to *Correct bounds* and its box turns green.
  5. *Grow N Bounds to Fit* grows each bounds that is too small to cover the mesh in every test pose, plus a margin of 5% of that box's longest side on every side, with Undo. Bounds are only ever grown, and ones that are already large enough are left alone. The list and boxes follow the renderers' bounds as they are now, so growing and Undo show up without a new check.
- **Test poses:** the current pose (as it is, before anything is moved), arms up, arms down, arms forward, arms back, legs apart, legs forward, legs back, bent forward / bent back / leaning left / leaning right (each with the arms up, to reach as far as possible), turned left, turned right. Each pose overrides a few muscles of the current pose and keeps the rest; muscles go from -1 to 1, the ends of Unity's default range.
- **Settings / options:** none. `Tolerance` (1 mm that a mesh may stick out) and `Margin` (5%) are constants in the code.
- **Caveats / known issues:**
  - Only humanoid muscles are moved. Bones outside the humanoid rig (tail, ears, wings, hair and skirt PhysBones), blend shapes that aren't set right now, and animations that move or scale bones are not tried: the margin is the only room they get. Blend shapes at their current weights are included, so set a large one before checking if it matters.
  - The poses stop at Unity's default muscle limits. IK and full-body tracking can bend an avatar a little further.
  - Without a humanoid Animator on the avatar only the current pose is checked, and the window says so.
  - The measured box belongs to the root bone the renderer had during the check. Check again after changing a root bone (for example with Root Bone and Anchor Fixer) or a mesh.
  - Bounds that are far too large are not reported or shrunk. VRChat's performance rank counts the size of all bounds together, so growing can raise it; the SDK's build panel shows the result.
  - With *Update When Offscreen* on, Unity recalculates the bounds every frame and ignores the stored ones. The tool checks and grows the stored bounds (they are what counts once the option is off) and marks the renderer in the list. Not confirmed: whether VRChat turns the option off on avatars.
  - Mesh renderers (not skinned) aren't checked: their bounds come from the mesh itself.
  - Tools that set bounds when the avatar is built (for example Modular Avatar's Mesh Settings, or Avatar Optimizer merging meshes) can override these values in the uploaded avatar.
  - The check takes about 1.5 seconds per million vertices (measured with ten meshes of 100,000 vertices), with no progress bar.

### Lighting Test Scene

- **File:** `Editor/Tools/LightingTestScene.cs`
- **Tests:** `Tests/Editor/LightingTestSceneTests.cs`. `LightingTestSceneTests`: scene contents, station lights (I's lamp behind and below), the reflection probe (only at H, not reaching G), moving with Undo and scene safety. `LightingTestSceneSwitchTests`: entering the test scene (the copy, the other scenes closed, rebuilding over assets left by an earlier build), returning (every scene reopened, the original avatar found, the copy not saved, deleted scenes skipped, nothing remembered) and deleting. `LightingTestSceneBakeTests`: bakes the scene (CPU lightmapper, since CI has no GPU) and reads each station's light probes to check the stations really differ: baked lamps light A, E, F and H, realtime lamps B, C and G stay out of the probes, A is warm and E neutral, F is red on one side and blue on the other, I is brighter facing back and down than facing front and up, H's reflection probe has a baked cubemap (skipped without a graphics device); it also checks the bake is still there after switching to the main scene and back. Takes longer than the other tests.
- **Menu:** Tools > HaTools > Lighting Test Scene (opens a window)
- **Purpose:** see how an avatar's shaders (lilToon, Poiyomi, ...) react to the kinds of world lighting found in VRChat, without uploading or launching VRChat.
- **How it works:**
  1. Pick the avatar in the scene in the window's *Avatar* field (it starts with the current selection). *Create Test Scene and Bake* offers to save the open scenes, builds the test scene at `Assets/HaTools/LightingTestScene/LightingTest.unity`, puts a copy of the avatar in it, closes your own scenes and starts an async bake. The scene has nine stations 15 m apart on the X axis:
     - A: baked warm point lamp. The avatar only receives it through light probes.
     - B: realtime warm lamp, render mode Not Important (vertex light).
     - C: realtime warm lamp, render mode Important (pixel light).
     - D: no lamp, only the dim flat ambient of a dark world.
     - E: baked neutral white lamp, as a colour reference for A.
     - F: baked red lamp on the avatar's left (-X) and blue lamp on its right (+X). The probes then hold light that changes with direction. A shader that shades by direction shows a red side and a blue side; one that flattens probe light into a single colour (lilToon averages it and works out one light direction) shows a mix.
     - G: realtime white lamp, render mode Important, several times brighter than C's: an overbright world.
     - H: E's baked white lamp plus a baked reflection probe covering the station (10 m box). The probe holds a pale sky colour, the floor and the back wall, so metallic and glossy materials have something to reflect. E shows the same light without it.
     - I: E's baked white lamp moved behind the avatar (-Z) and down to 0.3 m above the floor, a little to its right. The probes hold light that comes from behind and below, as in a world lit from the floor or from behind a stage. E shows the same lamp from the front.
     Each station has a static floor and back wall, a grid of 125 light probes, a label and a dynamic grey reference sphere that is lit the same way an avatar is. There is no skybox and, outside H, no reflection probe. An optional realtime sun is included but disabled (turning it on lights every station). The tool writes its materials and a fast, low-resolution `LightingTestSettings.lighting` asset (Progressive GPU) into the same folder.
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
  - A test scene kept from an older version of the tool doesn't have the stations added since (its button moves the avatar to an empty spot): use *Delete Test Scene* and create it again.
  - The Progressive GPU lightmapper falls back to CPU (slower) on unsupported GPUs.
  - Earlier versions wrote to `Assets/LightingTestScene/` (standalone draft) or `Assets/Kndra tools/LightingTestScene/` (before the rename to HaTools). Those folders can be deleted.
- **Reading the results:** some differences between shaders are their default settings, not bugs. lilToon defaults checked against its shader source (Lighting section of the material):
  - B: lilToon ignores vertex lights by default (Vertex Light Strength 0), so it looks like D at station B. Standard and other shaders are lit.
  - D: in a Linear colour space project (as VRChat uses) the ambient colour is about 0.005 in linear terms, below lilToon's Light Min Limit (0.05), so lilToon renders brighter than Standard there. This shows each shader's minimum brightness.
  - G: lilToon caps light at Light Max Limit (1), so it looks much the same as under a normal lamp; Standard and other shaders without a limit blow out to white.
  - H: only this station has a reflection probe. At every other station there is no skybox or probe, so reflections and environment-based effects see black: compare a shiny material at E and H. The probe bakes with the lighting, so it is black until the bake finishes.
  - I: Standard and other shaders that shade by direction light the back and the undersides (chin, under the arms) and leave the front to the dim bounced light. lilToon's main pass takes the probes' light direction with its vertical part made positive (`lilGetFixedLightDirection`: `abs` on Y), so light from below is treated as light from above: at I it shades as if lit from behind and above, never from under the chin. Its Backlight and its rim light's Light Direction Strength are both off by default; turn them on to see them react here.
  - Shadows aren't tested: no lamp casts shadows (lilToon also ignores cast shadows by default, Receive Shadow 0).
  - Comparing materials side by side: a renderer without an Anchor Override samples probes at its own bounds centre, and pixel/vertex light depends on distance to the lamp. Give the objects being compared the same Anchor Override and the same distance to the lamp, or the difference you see is partly position, not shader.

### Material Comparison

- **File:** `Editor/Tools/MaterialComparison.cs`
- **Tests:** `Tests/Editor/MaterialComparisonTests.cs`: the rows (identical materials, each kind of change being the only difference, keyword order, two different shaders matched by property name, textures without tiling), the filter and search, writing every kind of value with Undo, and the window noticing edits, Undo and a shader change.
- **Menu:** Tools > HaTools > Material Comparison (opens a window)
- **Purpose:** see exactly what differs between two materials (for example a body and a face material that should be lit the same way) and bring them in line without flipping between two inspectors.
- **How it works:**
  1. Pick two materials in the fields at the top (the window starts with the materials selected in the Project window). Each material has its own column: its preview sphere (drag to rotate), then one row per setting.
  2. The rows are the material settings (Shader, Render Queue, GPU Instancing, Double Sided GI, Keywords) followed by every property of the first material's shader in shader order, then the properties only the second material's shader has. A texture is followed by a `<name>_ST` row holding its tiling (X, Y) and offset (Z, W), unless the shader marks it `[NoScaleOffset]`. Labels are the shader's property names (`_MainTex`); hover for the name the inspector shows.
  3. A row whose values differ is highlighted in both columns. Two different shaders are matched by property name; a property one shader lacks shows "not in this shader" and counts as a difference. Values are compared exactly, with no rounding.
  4. Every value is an editable field (colour, slider or number, texture, vector, toggle, keyword text) and changes the material straight away, with Undo. The highlight updates as you edit, and changes made elsewhere (the material's inspector, Undo) show up within a moment.
  5. *Only differences* hides the rows that match. The search box filters by property name or inspector name. The count on the right says how many rows differ.
- **Settings / options:** none besides the filter and search.
- **Caveats / known issues:**
  - Editing writes the raw value, the way an animation does. It doesn't do the extra work a shader's own inspector does (turning on the keyword behind a toggle, lilToon's or Poiyomi's rendering mode presets, the Standard shader's blend settings for a Rendering Mode). For those, use the material's inspector and come back here to check the result.
  - The Shader row is read-only: change the shader in the material's inspector. Typing -1 as Render Queue goes back to the shader's own queue.
  - With *Only differences* on, a row you edit until it matches stays in the list (without its highlight) until the filter is used again (toggle it or change the search). Removing it mid-edit would move the keyboard focus to the row that takes its place.
  - Scrolling the list takes the keyboard focus off the field being edited, for the same reason. Only the rows in view are drawn, so shaders with a very long property list (Poiyomi) stay responsive; that hasn't been tried with Poiyomi or lilToon yet, only with Unity's built-in shaders.
  - Built-in materials and materials inside a model file are shown with their fields disabled: they can't be changed.
  - Not compared: override tags (`RenderType`, `VRCFallback`), and values saved in the material for properties its current shader doesn't have.
  - Poiyomi lists its section headers as properties too, so with it *Only differences* is the practical view.

### Package Search

- **Status:** in progress. Done: the window, its two tabs, the GitHub search and the result list with *Open in Browser*. Not done: the VPM listing check, Install / Remove, and the *Installed* tab.
- **File:** `Editor/Tools/PackageSearch.cs`
- **Tests:** `Tests/Editor/PackageSearchTests.cs`: the search address (qualifier and limit added, query escaped, blank query refused), reading GitHub's reply (fields, null description, at most 30 results in GitHub's order, empty replies) and the error messages (rate limit with the time to wait, offline, bad query, other codes). No network calls.
- **Menu:** Tools > HaTools > Package Search (opens a window)
- **Purpose:** find VRChat packages on GitHub and install the ones that publish a VPM listing into the open project, the way the VRChat Creator Companion (VCC) does, without leaving Unity.
- **How it works:**
  1. *Search* tab: type a query and press Enter (or *Search*). Each search is one call to GitHub's repository search API, never one per keystroke. `topic:vrchat` is added to the query to keep results to VRChat repos. The first 30 results are listed, in GitHub's order, each with its name, stars, description and an *Open in Browser* button.
  2. Planned: each result is checked for a VPM listing; results with one get a green *Install* button, then *Remove* once installed.
  3. Planned: *Installed* tab lists the packages in the `locked` section of `Packages/vpm-manifest.json` with their pinned versions and a *Remove* button. No network needed.
- **Settings / options:** none.
- **Caveats / known issues:**
  - GitHub allows 10 searches a minute without signing in (per network address). Past that the window says how many seconds to wait, from GitHub's `X-RateLimit-Reset` header.
  - Only repos tagged with the `vrchat` topic are found. GitHub ANDs qualifiers, so adding `topic:vpm` too would only find repos tagged with both.
  - Not tried against the live API yet: the cloud sessions can't reach GitHub's search endpoint, so the parsing is tested against replies written from GitHub's documented format.

### Root Bone and Anchor Fixer

- **File:** `Editor/Tools/RootBoneAndAnchorFixer.cs`
- **Tests:** `Tests/Editor/RootBoneAndAnchorFixerTests.cs`: the check (shared, different and missing root bones and anchors grouped with the renderers that use them, particles ignored), the override (every renderer including disabled ones, Undo, empty fields left alone) and the bounds conversion when the root bone changes.
- **Menu:** Tools > HaTools > Root Bone and Anchor Fixer (opens a window)
- **Purpose:** give every renderer of an avatar the same root bone and the same light anchor (Anchor Override). Renderers that sample lighting from different points look lit differently in VRChat; mismatched root bones give skinned meshes bounds in different places.
- **How it works:**
  1. Pick the avatar in the scene in the *Avatar* field (it starts with the current selection).
  2. *Check Root Bones and Light Anchors* shows two foldouts right below the button, *Root Bones* and *Light Anchors*, both closed. Each one's label says whether its value is shared by every renderer ("OK, shared by all N") or "not shared". Opening one lists every root bone or anchor in use, the most used first, as its path inside the avatar with the number of renderers using it; each of those is a foldout too, and opening it lists those renderers (click one to show it in the Hierarchy). Renderers without a root bone or anchor are listed together under *None*. Root bones only exist on skinned mesh renderers; light anchors are checked on mesh and skinned mesh renderers. A missing root bone or anchor never counts as shared, since each renderer then uses its own transform or bounds centre.
  3. Reference the root bone and light anchor to use in the *Root Bone* and *Light Anchor* fields (both must be inside the avatar), then *Override All Renderers*. It sets them on every mesh and skinned mesh renderer, including ones on disabled objects, with Undo, says how many renderers changed and runs the check again. An empty field leaves that setting as it is.
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

- 2026-10-04: Started Package Search (search GitHub, install VPM packages). Decisions made with the owner: everything stays inside Unity, with no call to the `vpm` command-line tool, since users can't be assumed to have it; look at the VPM Resolver package (`com.vrchat.core.vpm-resolver`, in every VRChat project) before writing any listing reading, dependency resolution or manifest writing of our own; write `vpm-manifest.json` the way the VCC does so both stay in step; v1 only installs packages whose dependencies are already met and says which are missing otherwise. Searching is one API call per Enter (GitHub's unsigned limit is 10 a minute); listing checks and downloads go to other hosts (GitHub Pages, release downloads), which don't count against it. Built in steps: window, result model and search first.
- 2026-10-03: Added VCC support: releases attach a VPM `.zip`, and a listing plus an "Add to VCC" page are published to GitHub Pages from the `gh-pages` branch. Done with two small Python scripts rather than VRChat's package template and listing action, to match the existing Unity-free release build. The listing is pushed to a branch instead of deployed with the Pages actions because that deployment's environment only accepts runs from the default branch, and a release runs from a tag. The first listing and the 0.3.1 `.zip` were published by hand, since 0.3.1 was released before this existed. Not tried in the VCC itself yet.
- 2026-10-03: Root Bone and Anchor Fixer: the check's result was a block of text and the owner found it unreadable. It is now two foldouts (root bones, light anchors), each holding one foldout per value in use with the renderers that use it. The foldouts show the check as it was when the button was pressed; they are not refreshed after Undo or edits made elsewhere.
- 2026-10-03: Bounds Fixer: the single *Show bounds in the Scene view* toggle and the flat list became two foldouts, *Incorrect bounds* and *Correct bounds*, asked for by the owner (the same shape as Root Bone and Anchor Fixer's check). Each renderer has a checkbox for its box, and each foldout has one that is on while any of its renderers is on. Unity has no control that links a parent checkbox to its children, so the foldout's state is worked out from the renderers each time it is drawn. Correct renderers start unchecked, so a full avatar doesn't fill the Scene view with green boxes. The yellow box has no checkbox of its own: it is drawn together with the red box of each incorrect renderer that is on.
- 2026-10-03: Added Material Comparison (the "Material comparison tool" idea). Layout chosen by Kndra: two inspector-like columns with editable fields, differing rows highlighted, and the two preview spheres on top. Fields write raw values through the `Material` API rather than `MaterialEditor.ShaderProperty`: custom drawers (Thry, lilToon) have varying heights and expect their own inspector, and fixed-height rows are what lets the window draw only the rows in view. Rows are rebuilt only when a material, its shader or its dirty count changes.
- 2026-10-03: Checking a window's look without opening Unity by hand: run the editor (not batch mode) on the throwaway test project with `-executeMethod`, open the window, and after a few seconds copy its pixels with the internal `GUIView.GrabPixels` (reflection, through `EditorWindow.m_Parent`) into a PNG, then `EditorApplication.Exit(0)`. The image comes out upside down. Keep that script out of the repository.
- 2026-10-03: Added Bounds Fixer (the "Bounds check" idea). Approach chosen by the owner: test poses on a hidden copy rather than one shared bounds for every renderer (redundant with Root Bone and Anchor Fixer), a reach estimate from bone lengths (covers every pose but asks for a 2.5 to 3 m cube on anything that reaches the hands) or the current pose only (finds nothing on an avatar at rest). The copy lives in a preview scene under a disabled object, so the user's scene isn't modified and no script on the copy runs. Arm muscle values were found by sweeping them on the test humanoid (T-pose is Down-Up 0.4, Front-Back 0.3; straight ahead is 0.2 / -0.6, not Front-Back -1, which crosses the arms). Not tried on a real avatar yet.
- 2026-10-03: Lighting Test Scene: added station I (E's lamp behind and below the avatar, the last station idea). One lamp for both directions rather than two stations; baked rather than realtime because lilToon builds its main light direction from the probes, while a realtime point light only reaches its add pass. Checked in lilToon's source (`lil_common_functions.hlsl`, `lil_common_macro.hlsl`): the main pass flips probe light from below to above. Not checked in Unity by eye with an avatar, only through the tests.
- 2026-10-03: Lighting Test Scene: added stations G (overbright realtime lamp) and H (reflection probe with E's lamp, so E is its control). The probe uses a solid sky colour rather than a skybox: a skybox is scene-wide and would give the dark stations a bright reflection. Not adding: two overlapping pixel lights and realtime shadows (lilToon-specific or off by default), and the contact sheet (dropped by the owner).
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
- Idea: Material Comparison follow-ups, not done: copy a value (or every difference) from one material to the other; compare override tags; link the rotation of the two preview spheres.
