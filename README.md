# 桌面工具

> **纯 AI 声明：本项目从需求分析、方案设计、代码编写、调试测试到文档撰写，全部由 AI 独立完成，无人工编写或修改。**

一个 Windows 轻量托盘小工具：双击桌面空白处隐藏桌面图标，再次双击恢复；可设置是否同时隐藏任务栏，也可设置开机启动。

[![Latest Release](https://img.shields.io/github/v/release/linrealp/DesktopHideIcon?label=release&sort=semver)](https://github.com/linrealp/DesktopHideIcon/releases/latest)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)](https://github.com/linrealp/DesktopHideIcon)
[![AI Generated](https://img.shields.io/badge/AI-100%25%20generated-8A2BE2)](https://github.com/linrealp/DesktopHideIcon)

## 下载

推荐直接使用最新发布版，无需安装或编译：

**[前往 GitHub Releases 下载 →](https://github.com/linrealp/DesktopHideIcon/releases/latest)**

当前版本为 **v1.0.0**，发布页提供：

| 发布文件 | 说明 |
| --- | --- |
| `DesktopHideIcon-v1.0.0.exe` | 免安装单文件版，下载后直接双击运行 |
| `DesktopHideIcon-v1.0.0-windows.zip` | 压缩包，内含 `桌面工具.exe` 和 `README.md` |
| `SHA256SUMS.txt` | 发布文件的 SHA-256 校验值 |

也可以使用 Git 获取完整源码：

```powershell
git clone https://github.com/linrealp/DesktopHideIcon.git
```

## 快速开始

1. 从 Releases 下载并运行 EXE；如果下载的是 ZIP，先解压再运行。
2. 程序只驻留在任务栏通知区域，不显示主窗口。
3. 双击桌面空白处，隐藏桌面图标；再双击一次恢复。
4. 右键通知区域图标，可修改设置或退出。

如果通知区域没有直接显示程序图标，点击任务栏右侧的 `^`，在“隐藏的图标”中即可找到。

## 功能

- **双击桌面空白处切换**：双击桌面非图标区域时，桌面图标和（可选）任务栏一起隐藏/恢复。
- **不误触**：双击桌面图标、任务栏、普通窗口时不会触发。
- **可选隐藏任务栏**：托盘菜单勾选“隐藏图标时同时隐藏任务栏”，默认关闭。开启后会同时隐藏所有显示器上的任务栏。
- **开机启动**：托盘菜单勾选“开机启动”，默认关闭。使用当前用户注册表启动项，不需要管理员权限。
- **自动恢复**：正常退出程序时，会自动恢复图标和任务栏。
- **异常恢复**：若程序被强制结束，重新运行一次 EXE，或执行 EXE `--restore`，即可恢复。
- **单实例**：重复运行不会产生多个后台进程。
- **轻量**：单个 42.5 KB 的 EXE，无需安装额外运行库，不联网，不常驻轮询。

## 托盘菜单

- `隐藏桌面图标` / `恢复桌面图标`：手动切换，和双击桌面效果相同。
- `隐藏图标时同时隐藏任务栏`：勾选后，下一次隐藏会连任务栏一起隐藏。若图标已经隐藏，勾选后会立即隐藏任务栏。
- `开机启动`：登录 Windows 后自动在托盘运行。
- `使用说明`：显示操作提示和恢复方法。
- `退出`：恢复图标/任务栏后退出。

菜单左侧带有勾选栏，蓝色对勾表示该选项已经开启。

双击托盘图标也可以手动切换隐藏状态，作为备用入口。

## 常见问题

**双击桌面空白处没有反应？**

确认程序仍在通知区域运行；桌面图标区域被其他窗口覆盖时，先按 `Win + D` 显示桌面。若任务栏正被隐藏，直接再次双击桌面空白处即可恢复。

**隐藏任务栏后找不到托盘图标？**

这是预期现象：任务栏和托盘一起隐藏了。直接双击桌面空白处恢复图标和任务栏。

**托盘菜单里怎么判断选项是否开启？**

菜单左侧会显示勾选框，有蓝色对勾表示已开启。例如“开机启动”打勾即表示已写入开机启动项。

**双击桌面图标会误触发吗？**

不会。双击“回收站”等桌面图标只会执行图标本身的动作，不会隐藏桌面。

**程序异常结束后桌面还是隐藏状态？**

重新运行 EXE，或执行：

```powershell
.\桌面工具.exe --restore
```

`--restore` 会直接恢复桌面图标和所有任务栏，然后退出，不会驻留后台。程序正常启动时也会检查 `%APPDATA%\DeskToggle\hidden.state`，发现上次异常状态会先尝试恢复。

## 开机启动说明

- 启动项位置：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- 值名：`DeskToggle`
- 值内容：带引号的当前 EXE 完整路径
- 这是“用户登录后启动”，不是 Windows 服务。
- 如果把 EXE 移动到其他目录，先运行一次新位置的 EXE，启动项路径会自动更新。

## 系统要求

- Windows 10 / Windows 11，64 位。
- 系统自带的 .NET Framework 4.8。Windows 11 已内置，无需另行安装。
- 标准 Windows Explorer 桌面。
- 不需要管理员权限。

## 配置与数据位置

| 内容 | 位置 |
| --- | --- |
| 配置 | `%APPDATA%\DeskToggle\settings.ini` |
| 异常退出恢复标记 | `%APPDATA%\DeskToggle\hidden.state` |
| 错误日志（仅出错时） | `%APPDATA%\DeskToggle\error.log` |
| 开机启动 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DeskToggle` |

配置项：

```ini
HideTaskbar=0
StartWithWindows=0
FirstRun=0
```

`1` 表示开启，`0` 表示关闭。程序会容错读取；配置损坏时会使用默认值启动。

## 卸载

1. 右键托盘图标，取消勾选“开机启动”。
2. 点击“退出”。
3. 删除 `桌面工具.exe` 或解压目录。
4. 可选：删除 `%APPDATA%\DeskToggle`。

程序不会写系统目录，不会安装服务，也不会修改自动隐藏任务栏等 Windows 设置。

## 开发与构建

源码使用 C# 5 + WinForms，针对 .NET Framework 4.8；直接使用 Windows 自带 `csc.exe` 编译，不需要 .NET SDK 或 NuGet 包。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出：

```text
dist\桌面工具.exe
```

图标可重新生成（需要 Python 3，仅开发时需要）：

```powershell
python tools\make_icon.py
```

运行冒烟测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\smoke-test.ps1
```

项目结构：

```text
src/        C# 源码
assets/     图标和应用程序清单
tools/      图标生成脚本
tests/      冒烟测试脚本
build.ps1   构建脚本
dist/       最终 EXE
README.md   项目说明
```

## 实现说明与限制

- 通过 `WH_MOUSE_LL` 监听左键双击；只读取点击坐标和前台窗口归属，不记录键盘输入。
- 桌面图标和任务栏使用 Win32 `ShowWindow` 临时隐藏/显示，不改变 Windows 的“显示桌面图标”或“自动隐藏任务栏”设置。
- 双击图标判断使用 Windows 辅助功能接口（MSAA），无需向 Explorer 进程写入内存。
- Explorer 重启后，托盘图标会自动重新注册；临时隐藏状态会被重置。
- 任务栏被完全隐藏时，Windows 不会自动调整已最大化窗口的工作区；这是显示行为，不影响恢复。
- 隐藏状态不跨程序重启保留；每次启动都从“图标和任务栏可见”的状态开始。
- 未做代码签名，首次运行可能出现 SmartScreen 提示；本程序没有网络功能，不会上传任何数据。

## 已验证行为

已在 Windows 11（build 26200）上实际验证：

- 桌面空白处双击隐藏/恢复图标。
- 同时隐藏/恢复所有任务栏。
- 双击回收站图标只会打开回收站，不会触发隐藏。
- 正常退出（包括任务栏隐藏时）自动恢复。
- 强制结束进程后，`--restore` 能恢复。
- 开机启动项写入、路径和取消删除。
- 托盘菜单勾选状态显示正常。
