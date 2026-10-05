[English](README.md) | 简体中文

# LabAPI-Mobile

本项目把 SCP: Secret Laboratory 的官方插件框架 [LabAPI](https://github.com/northwood-studios/LabAPI) 移植到 Carl Mod 服务端。Carl Mod 是 SCP:SL 的移动版，游戏代码大致相当于 SL 13.1-13.2。移植保留了 LabAPI 1.1.7 的 API，大多数 LabAPI 插件重新编译即可使用。

所有功能都在服务端运行。玩家继续使用原版 Carl Mod 安卓客户端，客户端不做任何修改，因此插件只能使用客户端本身已经支持的内容。

## 当前状态

- 适用于 Windows 版 Carl Mod 专用服务端，游戏版本 0.0.4。
- 包装类（wrapper）、事件参数和事件与官方 LabAPI 1.1.7 一致。每个事件都由一个 Harmony 补丁在与官方调用点等价的位置触发；全部 187 个补丁类在启动时应用，没有失败。
- [docs/compatibility.md](docs/compatibility.md) 列出了在 Carl Mod 上经过适配、行为近似或缺失的每个包装类、成员和事件（没有 SCP-3114，没有扬声器和文本玩具，使用 `KeycardPermissions` 等分支版本的类型……）。未列出的部分与官方 LabAPI 行为相同。
- 事件已在 Android 模拟器中运行的原版 Carl Mod 0.0.4 安卓客户端上验证，由 [EventProbe](tools/EventProbe/README.md) 记录每次事件调用。模拟器中没有触屏控件的操作，由 EventProbe 的服务端替代命令触发。测试环境见 [docs/testing.md](docs/testing.md)。
- 帧率目前只在模拟器中测量过，尚未在真机上测量。

## 在服务器上安装

要求：Windows 版 Carl Mod 专用服务端（已在游戏版本 0.0.4 上测试），以及安装器所需的 .NET Framework 4.6.2 或更高版本（Windows 10、Windows 11 和 Windows Server 2016 及以上版本自带）。

1. 停止服务器，把发布包 `LabApiMobile-<version>.zip` 解压到任意文件夹。
2. 以服务器文件夹（包含 `Carl Mod.exe` 的文件夹）为参数运行安装器：

   ```bat
   LabApiMobile.Installer.exe "C:\path\to\server"
   ```

   安装器会显示检测到的游戏版本；如果 `Assembly-CSharp.dll` 无法识别为 Carl Mod 服务端，它会拒绝修改，对未测试过的游戏版本会给出警告。它把 `Carl Mod_Data\Managed\Assembly-CSharp.dll` 备份为 `Assembly-CSharp.dll.labapi-original`，把 `LabApi.dll` 和 `0Harmony.dll` 复制到 `Carl Mod_Data\Managed`，并在 `ServerStatic.Awake` 中加入一次 `PluginLoader.Initialize()` 调用。其余改动都在运行时通过 Harmony 应用。
3. 在服务器自己的文件夹中启动服务器。日志中会出现 `[LabApi] [PATCHES] Applied ... (0 failed)` 以及已加载的插件。

更新 LabAPI-Mobile 或游戏更新后，再运行一次安装器即可；它总是基于原始文件打补丁。

卸载时，先停止服务器，然后运行：

```bat
LabApiMobile.Installer.exe "C:\path\to\server" --uninstall
```

这会恢复原始的 `Assembly-CSharp.dll` 并删除框架文件；插件和配置会保留。发布包中的 `INSTALL.txt` 包含同样的步骤。

### 数据文件夹

LabAPI-Mobile 的文件保存在 `<AppData>\SCP Secret Laboratory\LabAPI-Mobile\`，与官方游戏的 `LabAPI` 文件夹互不干扰：

| 路径 | 内容 |
| --- | --- |
| `plugins\global\`、`plugins\<port>\` | 插件 DLL，分别对所有端口或单个端口生效。 |
| `dependencies\global\`、`dependencies\<port>\` | 插件依赖的库。 |
| `configs\<port>\<plugin>\`、`configs\global\<plugin>\` | 插件配置。 |
| `configs\permissions.yml` | LabAPI 权限组（组名来自 `config_remoteadmin.txt`）。 |
| `LabApi-<port>.yml` | LabAPI 自身的设置（插件和依赖路径）。 |

`<AppData>` 是运行服务器的用户的 `%APPDATA%`。如果服务器文件夹中有内容为 `gamedir_for_configs: true` 的 `hoster_policy.txt`，`<AppData>` 就改为服务器文件夹内的 `AppData` 文件夹，与游戏自身配置的规则相同。该文件从工作目录读取，所以请在服务器文件夹中启动服务器。

## 插件

发布包只包含框架本身。安装插件时，把插件 DLL 复制到数据文件夹的 `plugins\global\`（所有端口）或 `plugins\<port>\`（单个端口），然后重启服务器。

- [projectmer-mobile](https://github.com/Michaelihc/projectmer-mobile)：移植到 Carl Mod 的 ProjectMER（MapEditorReborn 的 LabAPI 版本）。

为官方游戏的 LabAPI 编译的插件，需要针对本项目的 `LabApi.dll` 重新编译。

## 编写插件

插件就是针对本仓库 `LabApi.dll` 编译的普通 LabAPI 插件：

- 目标框架为 `net48`，引用 `LabApi.dll` 以及 `Carl Mod_Data\Managed` 中的游戏程序集（`Assembly-CSharp`、`Mirror`、`UnityEngine.*`、`CommandSystem.Core` 等），并设置 `Private="false"`。在 Carl Mod 中 MEC（`Timing`）位于 `DigitalDust.dll`。如果需要给游戏打补丁，以 `ExcludeAssets="runtime"` 引用 `Lib.Harmony` 2.3.6。[tools/EventProbe](tools/EventProbe/EventProbe.csproj) 和 [projectmer-mobile](https://github.com/Michaelihc/projectmer-mobile) 是可以参考的完整示例。
- API 就是官方 LabAPI 1.1.7：参见 [LabAPI 官方文档](https://github.com/northwood-studios/LabAPI/wiki) 和 [src/LabApi](src/LabApi) 中的源码。Carl Mod 上的差异见 [docs/compatibility.md](docs/compatibility.md)。
- 插件只能发送原版客户端能识别的内容：现有的角色、物品、预制体、管理员玩具（基础几何体、灯光、射击靶）、提示（hint）和广播。

手机是性能瓶颈：低端设备要渲染每个网络对象，并接收每个 SyncVar。

- 尽量减少网络对象数量，优先使用静态管理员玩具（`IsStatic`）；大量对象要分多帧生成。
- 不要给 SyncVar 写入未改变的值。
- 每帧、每次移动、每次射击或每条网络消息都会执行的代码中，不使用 LINQ、闭包、装箱或字符串格式化。
- 用事件或间隔合理的 MEC 协程代替 `Update` 循环。
- 只在 Unity 主线程访问游戏状态。

## 从源码构建

要求：.NET SDK（8 或更高版本）、Python 3，以及你自己的 Carl Mod 专用服务端 ZIP。本仓库不包含游戏文件；各项目针对解压到 `.runtime\server-original` 的服务端 `Carl Mod_Data\Managed` 程序集编译：

```powershell
python tools/extract-server.py --zip <path to the server ZIP>   # 只需一次：解压到 .runtime\server-original
dotnet build LabApiMobile.sln -c Release
.\tools\Package.ps1                                             # 发布包：dist\LabApiMobile-<version>.zip
```

`Package.ps1` 会构建解决方案，并生成 `dist\LabApiMobile-<version>.zip`，其中包含安装器、`framework\`（`LabApi.dll`、`0Harmony.dll`）、`INSTALL.txt`、`NOTICE.txt` 以及 `tools\package\` 中的许可证文本。开发时也可以直接从源码运行安装器：`dotnet run --project src/Installer -- <server-dir> <folder-with-LabApi.dll-and-0Harmony.dll>`。请对服务端的副本打补丁，不要修改 `.runtime\server-original`。

## 测试

[docs/testing.md](docs/testing.md) 介绍了本地测试服务器（`tools\Start-TestServer.ps1`，它会写入 `hoster_policy.txt`，让配置留在服务器文件夹内）、在 Android 模拟器中运行真实的 Carl Mod 客户端、双客户端、操作客户端界面以及帧时间测量。[tools/EventProbe](tools/EventProbe/README.md) 会记录每个 LabAPI 事件，并提供驱动测试的命令。

## 仓库结构

| 路径 | 内容 |
| --- | --- |
| `src/LabApi/` | LabAPI 移植。触发事件的 Harmony 补丁位于 `Events/Patches/<Area>/`。 |
| `src/LabApi.SourceGenerators/` | 官方 LabAPI 源代码生成器，并为每个事件加入 `Has<Event>` 检查。 |
| `src/Installer/` | 安装器（.NET Framework 4.6.2，Mono.Cecil）。 |
| `docs/` | 兼容性列表和测试指南。 |
| `tools/` | 服务端解压、测试服务器和 Android 模拟器脚本，EventProbe，打包（`Package.ps1`、`package/`）。 |
| `.refereces/` | 本地参考输入（服务端 ZIP、APK）；不在 Git 中。 |
| `.runtime/` | 解压后的服务端、测试服务器、日志和截图；不在 Git 中。 |
| `dist/` | 打包输出；不在 Git 中。 |

## 许可证

- `src/LabApi` 和 `src/LabApi.SourceGenerators` 是 Northwood Studios 的 LabAPI 的修改版本，采用 GNU Lesser General Public License v3.0 许可（[src/LabApi/LICENSE](src/LabApi/LICENSE)；它所补充的 GNU GPL v3.0 见 [tools/package/licenses/GPL-3.0.txt](tools/package/licenses/GPL-3.0.txt)）。
- 发布包附带 Harmony 和 Mono.Cecil，二者均采用 MIT 许可证；发布包中的 `NOTICE.txt` 列出了每个组件及其许可证文本。
- 本仓库的其余部分（安装器、工具、文档）目前还没有许可证文件。

## 免责声明

SCP: Secret Laboratory 是 Northwood Studios 开发的游戏。Carl Mod 是它的第三方移动版本。本项目与 Northwood Studios 及 Carl Mod 开发者没有任何关联，也未获得其认可。本仓库和发布包不包含任何游戏文件。
