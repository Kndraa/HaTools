# CLAUDE.md — Kndra tools

Guide for Claude and other agents (and humans) working on this repository. Read this before adding or changing anything. It holds the project philosophy, conventions, full tool documentation and running notes.

## What this project is

**Kndra tools** (`com.kndra.tools`) is a lightweight, editor-only Unity package of small tools for VRChat avatar creation. Every tool appears in Unity under **Tools > Kndra tools**.

- Target: Unity 2022.3, Built-in Render Pipeline (the VRChat setup).
- Editor-only: nothing here ships in avatar uploads or runs in-game.
- Tools should be quick to run. Avoid long blocking operations; if one is unavoidable, show a progress bar and let the user cancel.

## Philosophy

### 1. Modularity

Adding or changing a tool must not require editing files that belong to other tools.

- One tool = one file in `Editor/Tools/`. If a tool genuinely needs several files, give it its own subfolder: `Editor/Tools/<ToolName>/`.
- Tools do not depend on each other. Shared code goes in `Editor/Core/` only when at least two tools need it, and changes to Core must stay backwards compatible.
- A pull request for one tool should touch only that tool's file(s), its section in this document, and its line in `README.md`.

All scripts compile into one assembly (`Kndra.Tools.Editor`), so splitting code across files has no meaningful cost to compile time.

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
  Kndra.Tools.Editor.asmdef  Editor-only assembly for all scripts
  Core/
    KndraMenu.cs             Shared menu root constant
  Tools/                     <- all tools live here, one file (or subfolder) each
```

## Conventions

- **Namespace:** `Kndra.Tools`
- **Class names:** one static class per tool, named after the tool (e.g. `LightingTestScene`).
- **Menu path:** always build it from the shared constant, never type the root by hand:
  `[MenuItem(KndraMenu.Root + "Tool Name")]` or `[MenuItem(KndraMenu.Root + "Tool Name/Action")]`.
  Changing `KndraMenu.Root` renames the menu for every tool at once.
- **Undo:** any tool that changes scene objects or assets registers Undo (`Undo.RecordObject`, `Undo.RegisterCreatedObjectUndo`, ...).
- **Safety:** never modify the user's assets without asking first; write generated assets to a clearly named folder (e.g. `Assets/Kndra tools/<ToolName>/`).
- **Unity .meta files:** commit a `.meta` file for every file and folder. Unity creates them automatically when the package is opened in a project; commit those generated files. Never copy a `.meta` from another file (duplicate GUIDs break references).

## Adding a tool (checklist)

1. Create `Editor/Tools/<ToolName>.cs` (or a `<ToolName>/` subfolder).
2. Add the one-line header comment pointing to this file.
3. Use `KndraMenu.Root` for the menu path.
4. Add a section for the tool under **Tools** below.
5. Add one line for the tool under **Tools** in `README.md`.
6. Open the package in Unity once so `.meta` files are generated, and commit them.
7. Bump `version` in `package.json` (see Versioning).

## Versioning

Semantic versioning in `package.json`:

- Patch (`0.1.x`): bug fixes.
- Minor (`0.x.0`): new tools or new features in a tool.
- Major (`x.0.0`): breaking changes, such as removing a tool or changing the menu root.

## Tools

Full documentation for each tool. One `###` section per tool, in alphabetical order.

_No tools yet._

<!--
Template:

### <Tool name>

- **File:** `Editor/Tools/<ToolName>.cs`
- **Menu:** Tools > Kndra tools > <Tool name>
- **Purpose:** what problem it solves.
- **How it works:** step by step.
- **Settings / options:** if any.
- **Caveats / known issues:**
-->

## Notes

Running notes: decisions, ideas and things to remember. Newest first, each dated.

- 2026-09-27: Repository created. Package id `com.kndra.tools`, display name "Kndra tools", menu `Tools/Kndra tools/`. No tools yet.
- Idea: Lighting Test Scene tool (builds a scene with baked, vertex, pixel and ambient-only lighting stations to compare shaders such as lilToon and Poiyomi outside VRChat). A first draft exists; not yet added.
- Idea: Material comparison tool (show two materials' lighting settings side by side).
