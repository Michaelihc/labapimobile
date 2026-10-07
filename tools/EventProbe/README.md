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
`AutoThrottleLines`, `ReportIntervalSeconds`, `CancelEvents` (events whose `IsAllowed` is forced to `false`), `DeathRole`,
`MirrorFileConsoleToUnityLog`, `MaxLineLength`.

## Commands (server console and Remote Admin)

| Command | Effect |
| --- | --- |
| `probe cancel <Class.Event>` / `probe allow <Class.Event>` | Force `IsAllowed = false` on a cancellable event, or stop doing so. |
| `probe cancelled` / `probe counts` | List forced cancellations / call counts per event. |
| `probe where <playerId>` | Print a player's position and room. |
| `probe tp <playerId> <x> <y> <z>` | Move a player. |
| `probe yaw <playerId> <degrees>` | Turn a player to a world yaw (the client keeps its own pitch). With `probe tp`, gives repeatable views for captures and frame-time runs. |
| `probe as <playerId> <RA command>` | Run a Remote Admin command as the player (`probe as 2 mp select`), for commands that need a player sender. The reply is logged and also reaches the player's RA panel. |
| `probe elevator <playerId>` | Send the elevator nearest to the player to its next floor (server call of the chamber button), for repeatable rides. |
| `probe netobj <name part>` | List spawned network objects whose name contains the text: netId, observers, transform and admin toy state (static, SyncVars, colour). |
| `probe waypoints [refresh]` | Door waypoint ids on the server in netId order, and how many differ from the compact numbering a joining client computes; `refresh` requests the game's renumbering. |
| `probe toyrevert` | Creates a spawned primitive through the LabAPI wrapper, sets its colour and dynamic state in the same frame and reports one second later whether the server kept them. |
| `probe goto <playerId> <GameType> [index] [distance]` | Move a player in front of the n-th scene object of an Assembly-CSharp component type, e.g. `Scp079Generator`, `TeslaGate`, `Locker`, `AlphaWarheadNukesitePanel`, `Scp914Controller`. |
| `probe front <movedId> <anchorId> [distance]` | Move a player in front of another one (along the anchor's horizontal view), facing it. Default 1.5 m. |
| `probe face <playerId> <targetId> [away]` | Turn a player towards (or away from) another one. Yaw only: the client accepts no pitch override. |
| `probe aimat <playerId>` | Move a player, keeping its view direction, so the camera ray passes through the newest ragdoll. The player must look down. For SCP-049 resurrections and SCP-049-2 consumption. |
| `probe ragdollto <playerId>` | Move the newest ragdoll to where the player's camera points. |
| `probe equip <playerId> <ItemType>` | Give the item when missing and select it. |
| `probe deathrole <RoleTypeId\|None>` | Role given instead of Spectator on death (`ChangingRole` with `ChangeReason.Died`); also config `DeathRole`. |
| `probe killby <playerId> <attackerId>` | Kill a player with an SCP damage handler from the attacker (SCP-079 termination rewards, for example). |
| `probe cuff <disarmerId> <targetId> [release]` | Feed a `DisarmMessage` from the disarmer's connection to the server handler (the touch client cannot cuff). |
| `probe lunge <scp939Id> <targetId>` | Focus SCP-939 and feed a lunge hit command for the target to the lunge ability (no focus control on the touch HUD). |
| `probe atlas <scp106Id> <x> <y> <z>` | Feed a Hunter's Atlas destination to SCP-106's ability (the emulator renders an empty minimap). Needs vigor and a ready sinkhole cooldown. |
| `probe maxhp <playerId> [value]` | Print (or set) `Player.MaxHealth` with `Health` and `LifeId`. |
| `probe dropammo <playerId> <ItemType> <amount>` | Call `Player.DropAmmo` and print the pickups it returns. |
| `probe decon [offset]` | Print (or set) `Decontamination.Offset` with the controller's round start time and time offset. |
| `probe windows` | Compare `Window.List` with the scene's `BreakableWindow` objects. |
| `probe whoami` | Print `Player.Get(sender)` for the command sender. |
| `probe patches` | LabAPI patch summary: applied and failed patch classes, and any patched generic method. |
| `probe cassie <playerId> <words...>` | Sends a private CASSIE announcement, with matching subtitles and no glitches, through `Announcer.MessageTo`. Logs whether it was sent. |

The server-side stand-ins (`killby`, `cuff`, `lunge`, `atlas`) run the same server code a client message would reach,
so the LabAPI patches on that path are exercised with real clients connected; they do not test the client input.

From the server console the probe command has no prefix (`probe counts`); RA commands need `/` (`/give 2 11`).
