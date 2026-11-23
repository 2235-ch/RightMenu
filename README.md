# 右键菜单收藏管理

Windows x64 桌面工具。把常用图片、文档、文件夹、应用和终端放进资源管理器的经典右键菜单，关闭管理器后仍可使用。

- **放进「系统」**　普通右键后直接点击条目。
- **放进自定义分类**　例如「图片 → 角色三视图」，悬停后点击，用默认应用打开原图。
- **管理现有菜单**　按位置筛选、改名、隐藏和恢复支持的命令。首次打开会先备份，再自动接管支持的静态命令。

## 实机演示

**把项目资料收在一个分类里。** 「创作项目」同时收藏了素材文件夹、制作笔记和 PowerShell。右侧能看到每个条目的类型和菜单位置。

![收藏管理窗口，创作项目分类中有三个示例条目](assets/screenshots/favorites.png)

**从另一个目录打开参考图。** 在文件夹空白处右键，悬停「图片」，点击「角色三视图」即可打开收藏的原图。

![资源管理器经典右键菜单展开图片分类，显示角色三视图](assets/screenshots/context-menu.png)

**打开文件夹收藏。** 选择「项目素材」后，资源管理器直接进入目标目录，里面的参考图、场景草图和配色方案可以继续使用。

![资源管理器打开演示项目中的项目素材文件夹](assets/screenshots/open-folder.png)

## 下载与使用

在 GitHub Releases 下载 **RightMenu-v1.0-win-x64.zip**，解压并双击「右键菜单.exe」。当前用户安装，无需单独安装 .NET。升级前先关闭旧管理窗口。安装、菜单位置、备份、撤回接管和卸载步骤见[使用方法](使用说明.md)。

## 从源码构建

在 Windows x64 上安装支持 .NET 8 的 SDK，在仓库根目录运行：

```powershell
dotnet run --project tests/Acceptance.csproj -c Release
dotnet publish app/RightMenu.csproj -c Release -o dist
```

发布产物为 `dist/右键菜单.exe`。回归程序使用独立的临时注册表分支和测试目录。

---

如果这个小工具用着顺手，欢迎请我喝杯咖啡 ☕

<img src="assets/appreciation.png" alt="微信赞赏码" width="220">
