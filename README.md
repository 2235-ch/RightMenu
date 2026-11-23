# 右键菜单收藏管理

Windows x64 桌面工具。把常用图片、文档、文件夹、应用和终端放进资源管理器的经典右键菜单，关闭管理器后仍可使用。

- **放进「系统」**　普通右键后直接点击条目。
- **放进自定义分类**　例如「图片 → 角色三视图」，悬停后点击，用默认应用打开原图。
- **管理现有菜单**　按位置筛选、改名、隐藏和恢复支持的命令。首次打开会先备份，再自动接管支持的静态命令。

## 下载与使用

在 GitHub Releases 下载 **RightMenu-v1.0-win-x64.zip**，解压并双击「右键菜单.exe」。当前用户安装，无需单独安装 .NET。升级前先关闭旧管理窗口。安装、菜单位置、备份、撤回接管和卸载步骤见[使用方法](使用说明.md)。

## 从源码构建

在 Windows x64 上安装支持 .NET 8 的 SDK，在仓库根目录运行：

```powershell
dotnet run --project tests/Acceptance.csproj -c Release
dotnet publish app/RightMenu.csproj -c Release -o dist
```

发布产物为 `dist/右键菜单.exe`。回归程序使用独立的临时注册表分支和测试目录。

## 请我喝杯咖啡 ☕

如果这个小工具帮你少点了几次鼠标，欢迎请我喝杯咖啡。谢谢你的支持！

<img src="assets/appreciation.png" alt="微信赞赏码" width="220">
