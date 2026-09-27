# HaTool

Various tools useful for avatar creation in VRChat.

A lightweight, editor-only Unity package of small scripts that run quickly. They help you test and optimise avatars without launching VRChat, and speed up everyday avatar workflow. Everything appears in Unity under **Tools > HaTool**. Nothing from this package is included in avatar uploads.

## Requirements

- Unity 2022.3 (the VRChat version)

## Installation

- **VRChat Creator Companion:** coming soon.
- **.unitypackage:** download `HaTool-<version>.unitypackage` from the latest [release](../../releases) and open it in Unity (**Assets > Import Package > Custom Package...**). It installs into `Packages/com.kndra.hatool`.
- **Manual:** copy this repository into your project's `Packages/com.kndra.hatool` folder.

## Tools

- **Lighting Test Scene**: builds a scene with baked, vertex, pixel, ambient-only and red/blue split lighting stations so you can check how your avatar's shaders look under VRChat world lighting, plus a check of your renderers' light probe and Anchor Override settings.

## Contributing

Each tool lives in its own file under `Editor/Tools/`, so changes to one tool never touch another. See [CLAUDE.md](CLAUDE.md) for the project philosophy, conventions and full tool documentation.

## License

[MIT](LICENSE.md)
