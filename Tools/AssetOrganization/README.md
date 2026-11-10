# Unity 资产目录整理 — 2026-11-10

本次以当前本地工程为准，整理已导入的第三方包、重复目录和散落资产。没有更新依赖版本或同步 GitHub。

| 内容 | 归位目录 |
|---|---|
| VolFx 完整导入包 | `Assets/ThirdParty/VolFx` |
| Steamworks.NET 完整导入包 | `Assets/ThirdParty/Steamworks.NET` |
| 项目侧 SteamManager | `Assets/Script/Integrations/Steamworks.NET` |
| 自有预制体 | `Assets/Prefab`，原 `Prefabs/UI` 已并入 `Prefab/UI` |
| 自有材质 | `Assets/Material/UI`、`Assets/Material/Scene` |
| 自有 Shader | `Assets/Shader`，原 `Shaders/UI` 已并入 `Shader/UI` |
| 音频混音器 | `Assets/Audio/Mixers` |
| Input Actions | `Assets/Settings/Input` |
| URP 配置、Renderer 与全局设置 | `Assets/Settings/Rendering` |
| Volume Profile | `Assets/Settings/Volumes` |

Odin 与 DOTween 保留 `Assets/Plugins` 下的原始安装目录。Odin 自带的四个 `.unitypackage` 示例包仍在 `Assets/Plugins/Sirenix/Demos`，保留插件内部布局。TextMesh Pro、字体、场景和 Resources 的布局保持原样。

共执行 40 项资产／目录迁移，连同 `.meta` 移动。合并后清理了三个空根目录及其中一个空子目录。更新了 `OSMemoryHomeBuilder` 的混音器路径和 `OSMemoryFaceRepairAuthoring` 的预制体路径。

校验以整理开始时的文件清单为基准，覆盖已有未提交修改：

- 8,587 个保留文件全部核对；运行资产内容保留，仅主动修改了上述两处编辑器路径。
- Unity 重导入时自动升级了 83 个 VolFx 示例贴图的 `.meta` 序列化格式。已单独核对版本、默认空字段、旧平台名及一处示例轮廓浮点尾数变化；GUID、Sprite ID、切片矩形均保留。这 83 个示例贴图均不在正式场景／游戏 Resources 的依赖中。
- 所有迁移资产的 GUID 保留，无重复 GUID。
- 1,602 项正式场景及 Resources 依赖的 GUID 集合一致。
- Resources 相对路径及内容一致，脚本类型与所属程序集一致。
- 三个已加载正式场景均无 Missing Script，Unity 编译无错误。
- Play Mode 主菜单显示正常，25 个归位材质均有有效 Shader 且无 Shader 错误；正式自动存档与备份哈希保持一致，已退出 Play Mode。

详细校验、移动前 `.meta` 备份和本地改动快照位于 `Tools/AssetOrganization/Validation/20261110-053900`，沿用仓库现有规则忽略提交。完整迁移表保存在本目录的 `asset-moves-2026-11-10.json`。

后续自有资产沿用 `Script`、`Prefab`、`Shader`、`Material` 这些目录，避免再次出现单复数并存的根目录。第三方包保留包内结构；需要默认安装路径的插件继续放在其既有位置。
