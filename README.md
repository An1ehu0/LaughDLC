# 笑声DLC（LaughDLC）

一个只播放本地音频的 Windows 桌面小工具：选择 WAV/MP3，设置全局快捷键，按下快捷键即从头播放；关闭窗口后驻留系统托盘。

- 技术栈：C# / .NET 10 LTS / Windows Forms
- 音频：NAudio 3.2.0（仅直接引用 `NAudio.Core`、`NAudio.Wasapi`）
- 目标平台：Windows x64（实际验证环境：Windows 11 26100，x64）
- 发布：自包含、单文件、`PublishTrimmed=false`

## 项目结构

```
LaughDlc.sln
global.json
src/LaughDlc/
  LaughDlc.csproj
  Program.cs
  MainForm.cs
  MainForm.Designer.cs
  Controls/HotkeyRecorderControl.cs
  Interop/NativeMethods.cs
  Models/AppSettings.cs
  Models/HotkeyDefinition.cs
  Services/AudioPlayer.cs
  Services/HotkeyService.cs
  Services/SettingsStore.cs
  Services/SingleInstanceService.cs
  Services/AppLog.cs
  Properties/PublishProfiles/win-x64.pubxml
```

## 环境要求

开发/构建：

- Windows 10 22H2 或 Windows 11，x64
- .NET SDK 10.0.402（`global.json` 已固定；`rollForward=latestFeature`）

最终用户：

- 发布产物为自包含单文件，不需要预装 .NET Runtime。
- MP3 解码使用 Windows Media Foundation。Windows N 版系统如无 Media Feature Pack，MP3 可能无法解码；WAV 不受影响。

## 构建与运行

```powershell
dotnet build .\LaughDlc.sln -c Release
dotnet run --project .\src\LaughDlc\LaughDlc.csproj
```

## 发布

使用项目中的发布配置：

```powershell
dotnet publish .\src\LaughDlc\LaughDlc.csproj -p:PublishProfile=win-x64
```

或使用等价的显式参数（推荐命令）：

```powershell
dotnet publish .\src\LaughDlc\LaughDlc.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false
```

默认输出目录：

```
src\LaughDlc\bin\Release\net10.0-windows\win-x64\publish\LaughDLC.exe
```

发布配置明确保持：

- `RuntimeIdentifier=win-x64`
- `SelfContained=true`
- `PublishSingleFile=true`
- `PublishTrimmed=false`
- `IncludeNativeLibrariesForSelfExtract=true`（保证运行时原生库也进入单文件）

## 使用说明

1. 点击“选择音频…”，选择本地的 `.wav` 或 `.mp3`。
2. 点击“试听”播放；播放中再次点击“试听”或按下全局快捷键会停止当前播放并从头开始。
3. 点击“停止”停止播放。
4. 点击快捷键录入按钮，直接按下组合键（至少一个 Ctrl/Alt/Shift/Win 修饰键 + 一个普通按键）。`Esc` 取消录入。
5. 勾选/取消“启用全局快捷键”。
6. 快捷键状态会显示为“已启用（全局）”“已禁用”或“注册失败”，注册失败时会给出具体原因（例如系统保留、被其他程序占用）。

关闭主窗口默认隐藏到系统托盘，不退出进程。托盘菜单提供：

- 打开窗口
- 试听/播放
- 停止
- 启用/禁用快捷键
- 退出程序

托盘菜单状态会与窗口状态同步。只有“退出程序”会注销全局快捷键、停止并释放音频、移除托盘图标并退出进程。

## 快捷键实现说明

- 注册：`RegisterHotKey` / `UnregisterHotKey` + `WM_HOTKEY`，并附加 `MOD_NOREPEAT`。
- 录入 Win 修饰键：录入期间临时安装 `WH_KEYBOARD_LL` 低级键盘钩子，因此不依赖 WinForms `KeyDown` 对 Win 键不可靠的行为。
- 钩子生命周期：录入完成、按 Esc 取消、控件失焦、窗口隐藏/关闭、程序退出时均会解除；不会常驻安装低级键盘钩子。
- 快捷键事务：
  1. 先尝试注册新组合键；失败则保留旧配置、旧注册和当前启用状态。
  2. 注册成功后注销旧注册并保存配置。
  3. 保存失败时回滚到旧配置并重新注册旧快捷键；若旧快捷键也无法恢复，会明确提示“注册失败”而不会假装已启用。
- 系统保留组合键（例如 Win+K、Win+L、Ctrl+Alt+Del）无法注册时会明确提示并保持原配置。

## 配置文件

- 配置：`%AppData%\LaughDlc\settings.json`
- 日志：`%AppData%\LaughDlc\logs\app.log`
- 配置损坏/版本不兼容：原始文件会备份为 `settings.corrupt-*.json` 或 `settings.incompatible-v*-*.json`，程序恢复默认值、写入日志并在启动时提示。
- 配置不存在时使用默认值：默认快捷键 `Ctrl + Alt + L`，默认启用。
- 不复制用户音频；只保存路径。路径失效时程序仍可正常启动，并可重新选择文件。

配置示例：

```json
{
  "schemaVersion": 1,
  "audioFilePath": "D:\\audio\\laugh.wav",
  "hotkeyEnabled": true,
  "hotkey": {
    "modifiers": "Alt, Control",
    "key": "L"
  }
}
```

## 单实例策略

- 使用 `Mutex` 仅负责阻止多个主实例同时运行，不宣称 Mutex 能激活窗口。
- 第一次启动的实例创建命名管道服务；第二次启动时通过命名管道发送 `ACTIVATE` 请求（带连接超时与重试，覆盖首次启动竞态），已有实例显示、恢复并前置主窗口，第二个进程退出。
- 如果已有实例存在但管道在超时内无法连接，第二个进程会提示用户从任务栏/托盘打开已有窗口，然后退出。

## 支持格式与已知限制

支持：

- `.wav`（NAudio `WaveFileReader`）
- `.mp3`（Windows Media Foundation `MediaFoundationReader`）

不支持其他格式；即使容器内容可解码，只要扩展名不是 `.wav`/`.mp3` 也会拒绝，并提示重新选择。文件损坏、被占用、路径失效时会显示包含文件名和下一步操作的中文错误提示。

不包含（按需求范围）：

- 开机启动
- 随机播放
- 多音效管理
- 自动更新
- 麦克风注入/联网功能

其他限制：

- 使用 Windows 默认播放设备，不提供应用内音量控制。
- 关闭窗口后进程继续驻留托盘，需通过托盘菜单“退出程序”退出。

## 验证记录（2026-11-06）

构建：

- `dotnet build .\LaughDlc.sln -c Debug`：成功，0 警告、0 错误
- `dotnet build .\LaughDlc.sln -c Release`：成功，0 警告、0 错误

自动化/集成测试（临时测试程序引用正式项目，48 项全部通过）：

- 配置：缺失默认值、保存/读取、损坏 JSON 备份与恢复、版本不兼容恢复、非法快捷键恢复
- 快捷键事务：保存失败时保持旧配置、恢复旧注册、启用状态不丢失；禁用保存失败时恢复注册
- 快捷键注册：正常注册、重复注册冲突（Win32 1409）、注销后重新注册、WM_HOTKEY 实际触发、拒绝纯修饰键/无修饰键
- 音频：WAV 播放完成、MP3 播放完成、重复触发重播、停止释放、缺失文件/不支持扩展名/损坏 MP3 的错误提示

实际桌面操作验证：

- 通过录入控件设置 `Ctrl+Alt+J`，配置写入成功
- 通过录入控件捕获 `Win+Shift+F9` 并注册成功（验证 Win 键捕获）
- 尝试 `Win+K`：被系统保留，注册失败，旧配置保持不变，状态提示注册冲突
- 用独立进程占用 `Ctrl+Alt+M` 后尝试设置：冲突回滚，旧快捷键继续有效，配置未变化
- 禁用后独立进程可注册原组合键；启用冲突时保持禁用；解除冲突后重新启用成功
- 选择 `hahaha.mp3` 播放，状态显示正在播放；停止、再次触发快捷键、重复触发均正常
- 关闭窗口（`SC_CLOSE`，等价于 X/Alt+F4）后进程继续运行、窗口隐藏；启动第二个实例后第二个进程退出、已有窗口重新显示（句柄重建后 UIA 可见）
- 托盘“退出程序”实际执行后：日志记录完整清理、进程退出码 0、全局快捷键已注销（独立进程可重新注册）、互斥量已释放
- 真实启动时写入损坏 JSON：生成备份、恢复默认配置、程序正常启动

发布产物验证：

- 使用显式 `dotnet publish` 命令生成，输出目录中只有 `LaughDLC.exe`
- 文件大小 117,420,090 字节（约 111.98 MiB），PE 架构 `0x8664`（x64），文件版本 1.0.0.0
- SHA256：`B72C34E4B3E947B95048FF10AD82B55257C4765ABA7AD779288E473B16D18E70`
- 在当前 Windows 11 x64 环境直接启动发布 EXE，选择 MP3 播放、停止、快捷键触发、关闭隐藏、重复启动激活均正常
- `PublishTrimmed=false`，未启用裁剪

未验证项：

- 未在“未安装 .NET Runtime 的干净 Windows x64 虚拟机/设备”上运行发布产物。当前执行环境已安装 .NET 10 SDK/Runtime，因此不能声称已完成干净环境验证。
- 未进行人工听感验证（测试确认播放器进入 Playing/Completed 状态并成功驱动默认音频设备，但未通过人耳确认音量/内容）。
- 未在 Windows 10 实机验证；当前只在 Windows 11 26100 x64 验证。
- 托盘菜单的退出清理已实际执行；但通过 UIA 自动化稳定复现“鼠标右键托盘图标并点击菜单项”受当前共享桌面焦点影响，未形成可重复的自动化脚本。托盘图标、菜单行为和退出后的资源状态已分别确认。
- 前台焦点抢占（SetForegroundWindow 在系统焦点锁定时）只能尽力而为；窗口显示/恢复已确认。

干净 Windows 环境复现步骤：

1. 准备一台未安装 .NET Runtime 的 Windows 10 22H2 或 Windows 11 x64 虚拟机。
2. 将发布目录中的 `LaughDLC.exe` 单独复制到虚拟机。
3. 双击运行，确认无需安装 Runtime 即可启动。
4. 准备一个 WAV 和一个 MP3，验证选择、试听、停止、重复触发。
5. 录入 `Ctrl+Alt+J` 和 `Win+Shift+F9`，验证启停与重启后保留。
6. 用另一程序占用组合键，验证冲突回滚。
7. 关闭窗口验证托盘驻留，启动第二次验证已有窗口显示/前置。
8. 损坏 `%AppData%\LaughDlc\settings.json` 后重启，验证备份、默认恢复和提示。
9. 托盘菜单退出，确认进程、托盘图标、快捷键均已清理。
