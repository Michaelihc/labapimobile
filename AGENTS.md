# LabAPIMobile

Follow the parent workspace instructions. Reply in English unless requested otherwise.

LabAPI (Northwood's official SCP:SL plugin framework, LGPL-3.0) ported to the Carl Mod server, a mobile
fork of SCP:SL based on roughly SL 13.1-13.2 game code. Mods are server-side only: the Android client is
IL2CPP and is never patched. Plugins may use only content the stock Carl Mod client already understands.

## Layout

- `src/LabApi/` — the port. Namespaces, type names and member signatures follow official LabAPI 1.1.7.
- `src/LabApi/Events/Patches/<Area>/` — Harmony patches that raise LabAPI events and keep wrapper caches
  in sync. `PatchManager` applies them from `PluginLoader.Initialize()`.
- `src/LabApi.SourceGenerators/` — official generators, plus a `Has<Event>` subscriber check per event.
- `src/Installer/` — Cecil installer: copies `LabApi.dll`/`0Harmony.dll` and injects one
  `PluginLoader.Initialize()` call into `ServerStatic.Awake` (where official SL calls it).
- `docs/compatibility.md` — what is supported, adapted or absent compared with official LabAPI.
- `docs/testing.md` — local test servers, the Android emulator with the real client, frame-time measurement.
- `tools/` — server extraction, test-server and Android emulator scripts.
- `tools/EventProbe/` — dev plugin that logs every LabAPI event and has commands that drive client tests.
- `tools/Package.ps1`, `tools/package/` — framework-only release archive (installer, `LabApi.dll`,
  `0Harmony.dll`, `INSTALL.txt`, `NOTICE.txt`, licence texts) in `dist/`.
- `README.md` / `README.zh-CN.md` — English and Simplified Chinese READMEs; keep both in sync.
- `.runtime/` (ignored) — extracted server (`server-original`), test servers, logs, captures.
- `dist/` (ignored) — package output.

The ProjectMER port lives in its own repository (https://github.com/Michaelihc/projectmer-mobile); a local
checkout may sit next to this one as `../projectmer-mobile/`. It builds against this repository's `LabApi.dll`.

## Reference inputs

- `.refereces/` (ignored, not published) contains the supplied server ZIP and two APKs. Treat them as original reference inputs;
  write patched assemblies, rebuilt APKs and extracted files to separate output directories.
- `.refereces/scpsl-metarepo/` is a local junction to a `scpsl-plugins-metarepo` checkout; it is not published.
  It is external reference material. Do not edit through the junction without explicit authorization.
  Read its `AGENTS.md` and `.references/AGENTS.md` before researching APIs or native behavior.
- Official LabAPI source: `.refereces/scpsl-metarepo/.references/LabAPI/LabApi/`.
- Official SL 14.2.7 server decompilation (shows where the game raises each LabAPI event; grep
  `PlayerEvents.OnHurting(` etc.): `.refereces/scpsl-metarepo/.references/Decompiled/DedicatedServer/Assembly-CSharp/`.
- Carl Mod analysis workspace, a local sibling checkout at `../scpsl-mobile-analysis-20261005/` (not published):
  - `decompiled/server/` — fork Assembly-CSharp; `decompiled/extras/` — CarlModExtras;
    `decompiled/commands/` — CommandSystem.Core; MEC `Timing` lives in `DigitalDust.dll` in this fork.
  - `references/EXILED-8.2.1-sl13.2/` — EXILED for SL 13.2. Its `Exiled.Events/Patches` target game code
    close to this fork and are the best guide for patch targets and 13.x event semantics.
  - `references/MapEditorReborn-sl13.2/` — MapEditorReborn for SL 13.2, the same toy set as this fork.
- Latest EXILED: `.refereces/scpsl-metarepo/.references/Reference Plugins/EXILED/`.
- ProjectMER (LabAPI): `.refereces/scpsl-metarepo/.references/Reference Plugins/ProjectMER/`.

## Compatibility

Use the Carl Mod server assemblies to verify implementation signatures; the build compiles against
`.runtime/server-original/Carl Mod_Data/Managed` with publicized game assemblies. Similar type names in the
SL metarepo do not establish compatibility. Client C# exports in the analysis workspace are IL2CPP metadata
views with placeholder method bodies; inspect native code to establish client behavior.

## Porting rules

- Keep official LabAPI public API wherever the fork can support it, so LabAPI plugins port by recompiling.
  Adapt implementations to the fork's game code. When an official member uses a type the fork lacks but an
  older equivalent exists (e.g. `DoorPermissionFlags` → `KeycardPermissions`), keep the member name and use
  the fork type.
- Remove a wrapper, event or member only when its game feature is absent from the fork. Do not leave
  throwing stubs. Record removals and adaptations in `docs/compatibility.md`.
- Raise each event at the point equivalent to the official call site, with the same cancellation and
  argument-mutation semantics. Prefer prefix/postfix; use a transpiler only when the event must fire mid-method
  and fail soft if the IL pattern is missing. Do not patch methods small enough to be inlined.
- Each patch class carries one line citing the official call site (file and method in the 14.2.7
  decompilation) and targets the fork method by `[HarmonyPatch]` attributes.
- Wrapper lifecycle hooks that official SL exposes as static game events (`OnInstanceCreated`, `OnAdded`...)
  but the fork lacks are Harmony patches in `Events/Patches/Internal/`.

## Performance rules

Mobile clients are the bottleneck: low-end phones render every networked object and receive every SyncVar.

- Check `XEvents.Has<Event>` before allocating event args. No LINQ, closures, boxing or string formatting
  in per-frame, per-movement, per-shot or per-network-message paths.
- Do not add `Update` loops when an event or a timed MEC coroutine with a sensible interval suffices.
- Never dirty SyncVars with unchanged values. Prefer static toys (`IsStatic`), spawn large object sets
  across frames, and keep networked object counts low.
- All game-state access stays on the Unity main thread.

## Build and run

- `python tools/extract-server.py` extracts the reference server to `.runtime/server-original`.
- `dotnet build src/LabApi/LabApi.csproj -c Release`. Parallel agents add
  `--artifacts-path C:\tmp\labapi-<name>` so their obj/bin folders do not collide.
- The installer patches a server copy, never `server-original`:
  `dotnet run --project src/Installer -- <server-dir> <dir-with-LabApi.dll-and-0Harmony.dll>`.
- Start servers with `-batchmode -nographics -stdout -port<N>` (`-stdout` before `-port`) from the server
  directory, which needs `hoster_policy.txt` containing `gamedir_for_configs: true` so configs stay in
  `<server>/AppData` instead of the user's real `%APPDATA%\SCP Secret Laboratory`. Stop servers by PID.
- LabAPI data lives in `<appdata>/SCP Secret Laboratory/LabAPI-Mobile/` so it never collides with the
  official game's `LabAPI` folder.
