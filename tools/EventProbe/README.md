# EventProbe

Developer plugin that subscribes to every static event of every class in `LabApi.Events.Handlers` and writes one
line per call to the server console:

```
[PROBE] t=144.07 PlayerEvents.InteractingDoor Player=sdk_gphone64_x86_64(#2,FacilityGuard) Door=BreakableDoor:HczArmory/HCZ_ARMORY CanOpen=True IsAllowed=True
```

`t` is seconds since the plugin was enabled. Arguments are the event args' public properties; players, items,
pickups, doors and rooms are printed in a short form. Events that fire every frame or per packet (listed in
`ThrottledEvents`) log their first call and then a count per report interval; any other event that floods the log is
throttled automatically until it goes quiet.

It is a test tool: it makes every `Has<Event>` check true, so every patch runs its event path. Do not install it on a
server used for performance comparisons of LabAPI itself.

## Build and install

```powershell
dotnet build tools/EventProbe/EventProbe.csproj -c Release --artifacts-path C:\tmp\labapi-<name>
Copy-Item C:\tmp\labapi-<name>\bin\EventProbe\release\EventProbe.dll `
    ".runtime\server-emu\AppData\SCP Secret Laboratory\LabAPI-Mobile\plugins\global\"
```

The build also builds `src/LabApi`; install that `LabApi.dll` with `src/Installer` first.

With the file console (`Start-TestServer.ps1 -CommandSession emu`) the console text is not readable, so the probe
copies all console lines (including LabAPI loader and handler errors raised after it is enabled) into the Unity log
(`-LogPath`). Disable with `MirrorFileConsoleToUnityLog: false`.

## Config

`AppData/SCP Secret Laboratory/LabAPI-Mobile/configs/<port>/EventProbe/config.yml`: `ThrottledEvents`,
`AutoThrottleLines`, `ReportIntervalSeconds`, `CancelEvents` (events whose `IsAllowed` is forced to `false`),
`MirrorFileConsoleToUnityLog`, `MaxLineLength`.

## Commands (server console and Remote Admin)

| Command | Effect |
| --- | --- |
| `probe cancel <Class.Event>` / `probe allow <Class.Event>` | Force `IsAllowed = false` on a cancellable event, or stop doing so. |
| `probe cancelled` / `probe counts` | List forced cancellations / call counts per event. |
| `probe where <playerId>` | Print a player's position and room. |
| `probe tp <playerId> <x> <y> <z>` | Move a player. |
| `probe goto <playerId> <GameType> [index] [distance]` | Move a player in front of the n-th scene object of an Assembly-CSharp component type, e.g. `Scp079Generator`, `TeslaGate`, `Locker`, `AlphaWarheadNukesitePanel`, `Scp914Controller`. |

From the server console the probe command has no prefix (`probe counts`); RA commands need `/` (`/give 2 11`).
