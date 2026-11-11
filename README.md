[![English — selected](.github/assets/english-active.svg)](README.md) [![简体中文](.github/assets/chinese-inactive.svg)](README.zh-CN.md)

# UrHeart

The Unity project for **UrHeart**, built with **Unity 6000.4.11f1** and the **Universal Render Pipeline (URP)**.

This project includes the game code, scenes, assets, and project settings. **DOTween Pro, Odin Inspector, and VolFx are commercial plugins and are not included. You need your own licensed copies to use the project.** This applies to both the full project ZIP and the GitHub source.

## Get the project

### Option 1: Download the full project ZIP

Download **UrHeart-UnityProject-2026-11-10.zip** from the [Releases page](https://github.com/lettersama/UrHeart/releases#urheart-2026-11-10-en), then extract it to get the `UrHeart` folder.

This archive includes the fonts, TextMesh Pro font assets, `Packages`, and `ProjectSettings`. You do not need the separate font package. Any future cloud-drive mirror will use the same full project ZIP.

### Option 2: Get the source from GitHub

Clone this repository, or choose **Code → Download ZIP** on GitHub:

```bash
git clone https://github.com/lettersama/UrHeart.git
```

Then download **UrHeart-FontAssets-2026-11-10.zip** from the same [Releases page](https://github.com/lettersama/UrHeart/releases#urheart-2026-11-10-en-source). Extract it into the project root—the folder containing `Assets`, `Packages`, and `ProjectSettings`.

After extraction, you should have `Assets/Font/` and `Assets/Font.meta`. Keep every `.meta` file: these preserve font references in scenes, prefabs, and localization settings.

The font assets are large, so they are provided as Release attachments rather than stored in Git history. A normal `git pull` will not download these attachments. GitHub's automatically generated **Source code (zip)** also requires the separate font package.

## Install the commercial plugins

The versions below are the ones used by this project. Compatibility with other versions needs to be checked separately.

| Plugin | Version used | Installation path | Purpose |
| --- | --- | --- | --- |
| [DOTween Pro](https://assetstore.unity.com/packages/tools/visual-scripting/dotween-pro-32416) | 1.0.430 | `Assets/Plugins/Demigiant/` | UI animation, TMP text animation, and per-character shaking |
| [Odin Inspector and Serializer](https://odininspector.com/) | 4.0.2.4 | `Assets/Plugins/Sirenix/` | Inspector buttons, labels, lists, and other editor features |
| [VolFx – VFX Toolkit](https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/volfx-vfx-toolkit-post-processing-shaders-timeline-tools-300643) | 2.14.1 | `Assets/ThirdParty/VolFx/` | VHS, Glitch, Old Movie, and the framework used by custom post-processing effects |

1. Add the project in Unity Hub and open it with **6000.4.11f1**. Install the plugins above before running the game. Missing-type compilation errors are expected until the plugins are installed; you can handle the imports in Safe Mode.
2. DOTween Pro includes the DOTween core. The entire `Demigiant` directory is excluded from this distribution, so import both. Then open **Tools → Demigiant → DOTween Utility Panel → Setup DOTween**, and enable **UI** and **TextMesh Pro** support.
3. Import Odin Inspector. See the [official installation guide](https://odininspector.com/tutorials/getting-started/installing-odin-inspector) if needed.
4. Import the full VolFx package. Its default location is `Assets/VolFx/`; use Unity's Project window to move the whole folder to `Assets/ThirdParty/VolFx/`, preserving its asset GUIDs. Importing only the VHS effect is not enough.
5. Wait for Unity to finish importing and compiling. Check the Console after all plugins are installed, then open the game scene.

Alternatively, install the plugins in a temporary empty project using the same Unity version. Close both projects, then copy the complete plugin folders and their original `.meta` files to the paths above. Removing or regenerating those `.meta` files can break existing scene and renderer references.

Keep the project's existing renderer assets, Volume settings, and `Assets/Resources/DOTweenSettings.asset`. These are game-specific settings and do not need to be recreated when installing the plugins.

## Run the game

The repository's `steam_appid.txt` uses Valve's example game [Spacewar App ID `480`](https://partner.steamgames.com/doc/sdk/api/example) for Steam integration testing. Testing UrHeart's production achievements requires the corresponding App ID and Steamworks configuration.

1. Open `Assets/Scenes/GameSetUp.unity`.
2. To run locally without Steam, select the object with the `GameSetup` component and enable **Is Itch**. This skips Steam initialization and Steam integration, including achievements.
3. Check the release and test settings on `GameSetup`: **Is Demo** selects the demo content, while **Is Test** and the other test switches are for development. The saved project defaults are the full game, Steam integration enabled, and the normal startup flow.
4. Enter Play Mode.

The build scene order is `GameSetUp`, `Game_Main`, then `GameUI`. Keep the project's original `Packages` and `ProjectSettings`; copying only `Assets` into an empty project with different settings will not reproduce the full project setup.

## Verify downloads

The Releases page also provides `SHA256SUMS.txt`, `font-assets-manifest.json`, and `project-files-manifest.json`. On Windows, use `Get-FileHash <filename> -Algorithm SHA256` to check a ZIP against the supplied checksum.

## License

[MIT](LICENSE)

Third-party code and assets retain their own licenses. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
