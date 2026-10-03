# HaTools

Various tools useful for avatar creation in VRChat.

A lightweight, editor-only Unity package of small scripts that run quickly. They help you test and optimise avatars without launching VRChat, and speed up everyday avatar workflow. Everything appears in Unity under **Tools > HaTools**. Nothing from this package is included in avatar uploads.

## Requirements

- Unity 2022.3 (the VRChat version)

## Installation

- **VRChat Creator Companion:** coming soon.
- **.unitypackage:** download `HaTools-<version>.unitypackage` from the latest [release](../../releases) and open it in Unity (**Assets > Import Package > Custom Package...**). It installs into `Packages/com.kndra.hatools`.
- **Manual:** copy this repository into your project's `Packages/com.kndra.hatools` folder.

## Tools

- **Lighting Test Scene**: builds a scene with baked, vertex, pixel, ambient-only and red/blue split lighting stations so you can check how your avatar's shaders look under VRChat world lighting, plus a check of your renderers' light probe and Anchor Override settings.
- **Material Comparison**: shows two materials side by side, each with its preview and an editable field for every setting and shader property, and highlights the ones that differ. Can list only the differences.
- **Root Bone and Anchor Fixer**: checks whether all of your avatar's renderers share the same root bone and light anchor (Anchor Override), and sets them all to the ones you pick, with Undo.
- **Shader Fallback Preview**: shows a temporary copy of your avatar with the shaders VRChat falls back to when someone has your shaders blocked, with the Scene view camera in front of it, and lists what each material falls back to. One click removes it and puts the camera back.

## Contributing

Each tool lives in its own file under `Editor/Tools/`, so changes to one tool never touch another. See [CLAUDE.md](CLAUDE.md) for the project philosophy, conventions and full tool documentation.

## License

[MIT](LICENSE.md)
