# LabAPI-Mobile

[LabAPI](https://github.com/northwood-studios/LabAPI), the official plugin framework of SCP: Secret Laboratory, ported
to the Carl Mod server, a mobile fork of SCP:SL built on roughly SL 13.1-13.2 game code. It keeps the LabAPI 1.1.7
API, so most LabAPI plugins port by recompiling.

Everything runs on the server. Players keep the stock Carl Mod Android client, which is never modified, so plugins can
only use content that client already knows. The first plugin is a port of
[ProjectMER](src/ProjectMER/README.md) (MapEditorReborn for LabAPI).

## Install on a server

Requirements: the Carl Mod dedicated server for Windows (tested with game version 0.0.4) and .NET Framework 4.6.2 or
later for the installer, which is part of Windows 10, Windows 11 and Windows Server 2016 and later.

1. Stop the server and extract the release archive `LabApiMobile-<version>.zip` to any folder.
2. Run the installer with the server folder (the one that contains `Carl Mod.exe`):

   ```bat
   LabApiMobile.Installer.exe "C:\path\to\server"
   ```

   It prints the game version it found and refuses to patch a game build it does not recognise. It saves
   `Carl Mod_Data\Managed\Assembly-CSharp.dll` as `Assembly-CSharp.dll.labapi-original`, copies `LabApi.dll` and
   `0Harmony.dll` into `Carl Mod_Data\Managed`, and adds one `PluginLoader.Initialize()` call to `ServerStatic.Awake`.
   Everything else is applied at runtime with Harmony.
3. Start the server from its own folder. The log shows `[LabApi] [PATCHES] Applied ... (0 failed)` and the loaded
   plugins.

Run the installer again to update LabAPI-Mobile or after a game update; it always patches the original file.
`LabApiMobile.Installer.exe "C:\path\to\server" --uninstall` restores the original `Assembly-CSharp.dll` and removes the
framework files; plugins and configs are kept. `INSTALL.txt` in the archive has the same steps for server owners.

## Plugins and configs

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
`gamedir_for_configs: true` in the server folder, it is the `AppData` folder inside the server folder instead, as for the
game's own configs. The file is read from the working directory, so start the server from its folder.

## Writing plugins

A plugin is an ordinary LabAPI plugin built against this repository's `LabApi.dll`:

- Target `net48` and reference `LabApi.dll` plus the game assemblies from `Carl Mod_Data\Managed` (`Assembly-CSharp`,
  `Mirror`, `UnityEngine.*`, `CommandSystem.Core`, ...) with `Private="false"`; reference `Lib.Harmony` 2.3.6 with
  `ExcludeAssets="runtime"` if you patch the game. [`src/ProjectMER/ProjectMER.csproj`](src/ProjectMER/ProjectMER.csproj)
  and [`tools/EventProbe`](tools/EventProbe/README.md) are working examples.
- The API follows official LabAPI 1.1.7: see the [official LabAPI documentation](https://github.com/northwood-studios/LabAPI/wiki)
  and the source in [`src/LabApi`](src/LabApi). [docs/compatibility.md](docs/compatibility.md) lists every wrapper,
  member and event that is adapted, approximated or absent on Carl Mod (no SCP-3114, no speaker or text toys, fork
  types such as `KeycardPermissions`, ...).
- Plugins can only send what the stock client understands: existing roles, items, prefabs, admin toys (primitives,
  lights, shooting targets), hints and broadcasts.

Phones are the bottleneck: low-end devices render every networked object and receive every SyncVar.

- Keep networked object counts low and prefer static admin toys (`IsStatic`); spawn large sets over several frames.
- Never write a SyncVar with an unchanged value.
- No LINQ, closures, boxing or string formatting in code that runs every frame, movement, shot or network message.
- Use events or MEC coroutines with a sensible interval instead of `Update` loops.
- Touch game state only on the Unity main thread.

## Building

Requirements: the .NET SDK (8 or later) and Python 3. The projects compile against the Carl Mod server's assemblies,
extracted from the reference server ZIP in `.refereces/`:

```powershell
python tools/extract-server.py                 # once: extracts the server to .runtime\server-original
dotnet build LabApiMobile.sln -c Release
.\tools\Package.ps1                            # release archive: dist\LabApiMobile-<version>.zip
```

`Package.ps1` builds the solution and writes `dist\LabApiMobile-<version>.zip` with the installer, `framework\`
(`LabApi.dll`, `0Harmony.dll`), `plugins\ProjectMER.dll`, `INSTALL.txt`, `NOTICE.txt` and the licence texts from
`tools\package\`. During development the installer also runs from source:
`dotnet run --project src/Installer -- <server-dir> <folder-with-LabApi.dll-and-0Harmony.dll>`. Patch a copy of the
server, never `.runtime\server-original`.

## Testing

[docs/testing.md](docs/testing.md) covers local test servers (`tools\Start-TestServer.ps1`, which writes
`hoster_policy.txt` so nothing goes to your real `%APPDATA%`), the Android emulator with the real Carl Mod client, frame
time measurements and the ProjectMER test fixtures. [tools/EventProbe](tools/EventProbe/README.md) logs every LabAPI
event and has commands that drive tests.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/LabApi/` | The LabAPI port. Harmony patches that raise events are in `Events/Patches/<Area>/`. |
| `src/LabApi.SourceGenerators/` | Official LabAPI source generators, plus a `Has<Event>` check per event. |
| `src/Installer/` | The installer (.NET Framework, Mono.Cecil). |
| `src/ProjectMER/` | The ProjectMER port and its documentation. |
| `docs/` | Compatibility list, testing guide, ProjectMER port notes. |
| `tools/` | Server extraction, test server and Android emulator scripts, EventProbe, packaging (`Package.ps1`, `package/`). |
| `.refereces/` | Reference inputs (server ZIP, APKs, SCP:SL metarepo junction); not in Git. |
| `.runtime/` | Extracted server, test servers, logs and captures; not in Git. |
| `dist/` | Package output; not in Git. |

## Licences

- `src/LabApi` and `src/LabApi.SourceGenerators` are a modified version of Northwood Studios' LabAPI and are licensed
  under the GNU Lesser General Public License v3.0
  ([tools/package/licenses/LGPL-3.0.txt](tools/package/licenses/LGPL-3.0.txt), supplementing the GPL v3.0 in the same
  folder).
- `src/ProjectMER` is a port of [ProjectMER](https://github.com/Michal78900/ProjectMER) by Michal78900 and
  contributors. That repository publishes no licence file; its predecessor MapEditorReborn states CC BY-SA 3.0 in its
  source file headers.
- The release archive bundles Harmony and Mono.Cecil (both MIT); `NOTICE.txt` in the archive lists every component.
- SCP: Secret Laboratory is by Northwood Studios; Carl Mod is a third-party mobile version. This repository and the
  release archive contain no game files.
