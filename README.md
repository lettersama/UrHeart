# UrHeart

UrHeart 的 Unity 工程，使用 **Unity 6000.4.11f1**，渲染管线为 URP。

工程保留游戏代码、场景、资源和项目配置。**DOTween Pro、Odin Inspector 和 VolFx 商业插件未随工程提供，需要自行取得授权并安装。** 完整工程 ZIP 与 GitHub 源码都遵循这一点。

## 获取工程

### 方式一：下载完整工程 ZIP

从 [工程发布页](https://github.com/lettersama/UrHeart/releases/tag/project-2026-11-10) 下载 **UrHeart-UnityProject-2026-11-10.zip**，解压后得到 `UrHeart` 文件夹。

这个包已包含字体、TextMesh Pro 字体资产、`Packages` 和 `ProjectSettings`，不需要另外下载字体补充包。网盘镜像后续提供时，将使用同一份 ZIP。

### 方式二：从 GitHub 获取

克隆本仓库，或使用 GitHub 的 **Code → Download ZIP**：

```bash
git clone https://github.com/lettersama/UrHeart.git
```

然后从同一个[工程发布页](https://github.com/lettersama/UrHeart/releases/tag/project-2026-11-10)下载 **UrHeart-FontAssets-2026-11-10.zip**，解压到工程根目录，也就是包含 `Assets`、`Packages`、`ProjectSettings` 的目录。

解压后应有 `Assets/Font/` 和 `Assets/Font.meta`。请保留所有 `.meta` 文件，它们用于恢复场景、预制体和本地化配置中的字体引用。

字体资源体积较大，放在 Releases 附件中，不进入 Git 历史。普通的 `git pull` 不会下载这些附件。GitHub 自动生成的 **Source code (zip)** 也不包含字体，需要单独安装补充包。

## 安装商业插件

下面记录的是当前工程使用的版本，其他版本需要自行验证兼容性。

| 插件 | 工程使用版本 | 安装位置 | 用途 |
| --- | --- | --- | --- |
| [DOTween Pro](https://assetstore.unity.com/packages/tools/visual-scripting/dotween-pro-32416) | 1.0.430 | `Assets/Plugins/Demigiant/` | UI 动画、TMP 文字动画和逐字抖动 |
| [Odin Inspector and Serializer](https://odininspector.com/) | 4.0.2.4 | `Assets/Plugins/Sirenix/` | Inspector 按钮、标签和列表等编辑器功能 |
| [VolFx – VFX Toolkit](https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/volfx-vfx-toolkit-post-processing-shaders-timeline-tools-300643) | 2.14.1 | `Assets/ThirdParty/VolFx/` | VHS、Glitch、Old Movie 和自定义后处理所依赖的框架 |

1. 用 Unity Hub 添加工程，并选择 **6000.4.11f1**。先安装上述插件，再运行游戏。插件缺失时出现相关类型不存在的编译错误是预期现象，可以在 Safe Mode 中处理导入。
2. DOTween Pro 同时提供 DOTween 核心。本工程将整个 `Demigiant` 目录排除，需要一并导入。导入后运行 **Tools → Demigiant → DOTween Utility Panel → Setup DOTween**，启用 **UI** 与 **TextMesh Pro** 支持。
3. 导入 Odin Inspector。[官方安装说明](https://odininspector.com/tutorials/getting-started/installing-odin-inspector)可供参考。
4. 导入 VolFx。原包默认位于 `Assets/VolFx/`，请在 Unity 的 Project 窗口中把整个目录移到 `Assets/ThirdParty/VolFx/`，保留资源的 GUID。不要只导入单个 VHS 效果。
5. 等待 Unity 完成导入和编译。插件安装完整后，检查 Console，再打开游戏场景。

也可以先在同版本的临时空工程中安装插件，再关闭两个工程，把完整插件目录和原有 `.meta` 文件复制到上表位置。不要删除或重新生成插件的 `.meta`，否则现有场景和渲染器引用可能失效。

请保留工程自带的渲染器、Volume 配置和 `Assets/Resources/DOTweenSettings.asset`。这些是游戏自己的配置，安装插件时无需重建。

## 运行

1. 打开 `Assets/Scenes/GameSetUp.unity`。
2. 如果只想在本地查看游戏、不接入 Steam，选中挂有 `GameSetup` 组件的对象，勾选 **Is Itch**。该开关会跳过 Steam 初始化与成就接入。
3. 检查 `GameSetup` 的发行和测试开关：**Is Demo** 决定是否运行 Demo 内容；**Is Test** 等测试开关用于开发。当前工程保存的是完整版、Steam 接入开启、正式启动流程。
4. 进入 Play Mode。

工程的构建场景顺序为 `GameSetUp`、`Game_Main`、`GameUI`。请保留项目原有的 Packages 和 ProjectSettings；不要把 Assets 单独导入一个配置不同的空工程来代替完整工程。

## 下载校验与资源记录

工程发布页同时提供 `SHA256SUMS.txt`、`font-assets-manifest.json` 和 `project-files-manifest.json`。Windows 下可用 `Get-FileHash 文件名 -Algorithm SHA256` 核对 ZIP。

第三方素材的现有来源和许可记录保留在 `ThirdPartyNotices/`，Steamworks.NET 的许可保留在其原目录。商业插件请通过各自的官方渠道获取。

## Setup in English

Use **Unity 6000.4.11f1**. Download **UrHeart-UnityProject-2026-11-10.zip** from the [project release](https://github.com/lettersama/UrHeart/releases/tag/project-2026-11-10) for the project with fonts included. If you clone this repository or use GitHub's generated source ZIP, also extract **UrHeart-FontAssets-2026-11-10.zip** into the project root. Preserve all `.meta` files.

Commercial dependencies are **not included** in either distribution: install your own licensed copies of **DOTween Pro 1.0.430**, **Odin Inspector 4.0.2.4**, and **VolFx 2.14.1** at the paths listed above. Run DOTween Setup with UI and TextMesh Pro support. The tested versions are recorded here; compatibility with other versions has not been verified.

After compilation finishes, open `Assets/Scenes/GameSetUp.unity`. Enable **Is Itch** on `GameSetup` for local testing without Steam, then enter Play Mode.
