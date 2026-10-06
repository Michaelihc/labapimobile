English | [简体中文](README.zh-CN.md)

# LabAPI-Mobile

[LabAPI](https://github.com/northwood-studios/LabAPI), the official plugin framework of SCP: Secret Laboratory, ported
to the Carl Mod server, a mobile version of SCP:SL built on roughly SL 13.1-13.2 game code. It keeps the LabAPI 1.1.7
API, so most LabAPI plugins port by recompiling.

Everything runs on the server. Players keep the stock Carl Mod Android client, which is never modified, so plugins can
only use content that client already knows.

## Status

- Targets the Carl Mod dedicated server for Windows, game version 0.0.4.
- Wrappers, event arguments and events follow official LabAPI 1.1.7. Each event is raised by a Harmony patch at the
  point equivalent to the official call site; all 187 patch classes apply at startup with no failures.
- [docs/compatibility.md](docs/compatibility.md) lists every wrapper, member and event that is adapted, approximated
  or absent on Carl Mod (no SCP-3114, no speaker or text toys, fork types such as `KeycardPermissions`, ...). Anything
  not listed there works as in official LabAPI.
- Events are verified with the stock Carl Mod 0.0.4 Android client running in an Android emulator, with
  [EventProbe](tools/EventProbe/README.md) logging every event call. Actions that have no touch control in the
  emulator are driven by EventProbe's server-side stand-ins. [docs/testing.md](docs/testing.md) describes the setup.
- Frame rates have been measured in the emulator only, not on physical phones.

## Install on a server

Requirements: the Carl Mod dedicated server for Windows (tested with game version 0.0.4) and .NET Framework 4.6.2 or
later for the installer, which is part of Windows 10, Windows 11 and Windows Server 2016 and later.

1. Stop the server and extract the release archive `LabApiMobile-<version>.zip` to any folder.
2. Run the installer with the server folder (the one that contains `Carl Mod.exe`):

   ```bat
   LabApiMobile.Installer.exe "C:\path\to\server"
   ```

   It prints the game version it found, refuses to patch an `Assembly-CSharp.dll` it does not recognise as the Carl
   Mod server and warns about untested game versions. It saves `Carl Mod_Data\Managed\Assembly-CSharp.dll` as
   `Assembly-CSharp.dll.labapi-original`, copies `LabApi.dll` and `0Harmony.dll` into `Carl Mod_Data\Managed` (an
   existing file of the same name, such as another mod's `0Harmony.dll`, is kept as `<file>.labapi-original`), and
   adds one `PluginLoader.Initialize()` call to `ServerStatic.Awake`. Everything else is applied at runtime with
   Harmony.
3. Start the server from its own folder. The log shows `[LabApi] [PATCHES] Applied ... (0 failed)` and the loaded
   plugins.

Run the installer again to update LabAPI-Mobile or after a game update; it always patches the original file.

To uninstall, stop the server and run:

```bat
LabApiMobile.Installer.exe "C:\path\to\server" --uninstall
```

This restores the original `Assembly-CSharp.dll`, removes the framework files and restores any file the installer
replaced; plugins and configs are kept. An interrupted install or uninstall can simply be run again.
`INSTALL.txt` in the archive has the same steps.

### Data folder

LabAPI-Mobile keeps its files in `<AppData>\SCP Secret Laboratory\LabAPI-Mobile\`, separate from the official game's
`LabAPI` folder:

| Path | Contents |
| --- | --- |
| `plugins\global\`, `plugins\<port>\` | Plugin DLLs, for every port or for one port. |
| `dependencies\global\`, `dependencies\<port>\` | Libraries that plugins need. |
| `configs\<port>\<plugin>\`, `configs\global\<plugin>\` | Plugin configs. |
| `configs\permissions.yml` | LabAPI permission groups (group names come from `config_remoteadmin.txt`). |
| `LabApi-<port>.yml` | LabAPI's own settings (plugin and dependency paths). |

`<AppData>` is `%APPDATA%` of the user running the server. With a `hoster_policy.txt` containing
`gamedir_for_configs: true` in the server folder, it is the `AppData` folder inside the server folder instead, as for
the game's own configs. The file is read from the working directory, so start the server from its folder.

## Plugins

The release archive contains the framework only. Install a plugin by copying its DLL into `plugins\global\` (every
port) or `plugins\<port>\` (one port) in the data folder and restarting the server.

- [projectmer-mobile](https://github.com/Michaelihc/projectmer-mobile): ProjectMER (MapEditorReborn, LabAPI edition)
  ported to Carl Mod.

Plugins built for the official game's LabAPI must be rebuilt against this `LabApi.dll`.

## Writing plugins

A plugin is an ordinary LabAPI plugin built against this repository's `LabApi.dll`:

- Target `net48` and reference `LabApi.dll` plus the game assemblies from `Carl Mod_Data\Managed` (`Assembly-CSharp`,
  `Mirror`, `UnityEngine.*`, `CommandSystem.Core`, ...) with `Private="false"`. MEC (`Timing`) is in `DigitalDust.dll`
  on Carl Mod. Reference `Lib.Harmony` 2.3.6 with `ExcludeAssets="runtime"` if you patch the game.
  [tools/EventProbe](tools/EventProbe/EventProbe.csproj) and
  [projectmer-mobile](https://github.com/Michaelihc/projectmer-mobile) are working examples.
- The API is official LabAPI 1.1.7: see the [official LabAPI documentation](https://github.com/northwood-studios/LabAPI/wiki)
  and the source in [src/LabApi](src/LabApi). Differences on Carl Mod are in
  [docs/compatibility.md](docs/compatibility.md).
- Plugins can only send what the stock client understands: existing roles, items, prefabs, admin toys (primitives,
  lights, shooting targets), hints and broadcasts.

Phones are the bottleneck: low-end devices render every networked object and receive every SyncVar.

- Keep networked object counts low and prefer static admin toys (`IsStatic`); spawn large sets over several frames.
- Never write a SyncVar with an unchanged value.
- No LINQ, closures, boxing or string formatting in code that runs every frame, movement, shot or network message.
- Use events or MEC coroutines with a sensible interval instead of `Update` loops.
- Touch game state only on the Unity main thread.

## Building from source

Requirements: the .NET SDK (8 or later), Python 3 and your own copy of the Carl Mod dedicated server ZIP. The game
files are not in this repository; the projects compile against the server's `Carl Mod_Data\Managed` assemblies,
extracted to `.runtime\server-original`:

```powershell
python tools/extract-server.py --zip <path to the server ZIP>   # once: extracts to .runtime\server-original
dotnet build LabApiMobile.sln -c Release
.\tools\Package.ps1                                             # release archive: dist\LabApiMobile-<version>.zip
```

`Package.ps1` builds the solution and writes `dist\LabApiMobile-<version>.zip` with the installer, `framework\`
(`LabApi.dll`, `0Harmony.dll`), `INSTALL.txt`, `NOTICE.txt` and the licence texts from `tools\package\`. During
development the installer also runs from source:
`dotnet run --project src/Installer -- <server-dir> <folder-with-LabApi.dll-and-0Harmony.dll>`. Patch a copy of the
server, never `.runtime\server-original`.

## Testing

[docs/testing.md](docs/testing.md) covers local test servers (`tools\Start-TestServer.ps1`, which writes
`hoster_policy.txt` so configs stay in the server folder), the Android emulator with the real Carl Mod client, two
clients, driving the client UI and frame-time measurement. [tools/EventProbe](tools/EventProbe/README.md) logs every
LabAPI event and has commands that drive tests.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/LabApi/` | The LabAPI port. Harmony patches that raise events are in `Events/Patches/<Area>/`. |
| `src/LabApi.SourceGenerators/` | Official LabAPI source generators, plus a `Has<Event>` check per event. |
| `src/Installer/` | The installer (.NET Framework 4.6.2, Mono.Cecil). |
| `docs/` | Compatibility list and testing guide. |
| `tools/` | Server extraction, test server and Android emulator scripts, EventProbe, packaging (`Package.ps1`, `package/`). |
| `.refereces/` | Local reference inputs (server ZIP, APKs); not in Git. |
| `.runtime/` | Extracted server, test servers, logs and captures; not in Git. |
| `dist/` | Package output; not in Git. |

## Licences

This repository is licensed under the GNU Lesser General Public License v3.0 ([COPYING.LESSER](COPYING.LESSER), which
supplements the GNU General Public License v3.0 in [COPYING](COPYING)), the licence of the LabAPI it is built from.

- `src/LabApi` and `src/LabApi.SourceGenerators` are a modified version of Northwood Studios' LabAPI, which is also
  LGPL-3.0 ([src/LabApi/LICENSE](src/LabApi/LICENSE)).
- The release archive bundles Harmony and Mono.Cecil, both under the MIT License; `NOTICE.txt` in the archive lists
  every component and its licence text.

## Disclaimer

SCP: Secret Laboratory is a game by Northwood Studios. Carl Mod is a third-party mobile version of it. This project is
not affiliated with or endorsed by Northwood Studios or the Carl Mod developers. This repository and the release
archive contain no game files.
