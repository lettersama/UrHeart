# 发布工程打包

先安装字体补充包，并将准备发布的修改提交到 Git。在工程根目录运行：

```powershell
python Tools/Distribution/build_archives.py --version 2026-11-10 --output ../UrHeart_Release
```

脚本使用 Git 跟踪的文件和本机 `Assets/Font` 生成完整工程 ZIP、字体补充包、逐文件清单和 SHA-256 校验文件。完整工程 ZIP 内的根目录固定为 `UrHeart/`；字体包直接解压到已有工程根目录。

输出目录必须在工程外，已有同名产物时脚本会停止。关闭 Unity 后打包，避免文件在压缩过程中变化。

商业插件目录受 `.gitignore` 保护，不进入发布包。脚本还会检查已跟踪的商业插件目录及三套插件的 Asset Store productId，发现遗漏就停止。不要使用 `git add -f` 添加这些插件，也不要上传本机验证工程。
