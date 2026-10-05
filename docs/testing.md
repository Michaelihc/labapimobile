# Testing the Android client against a local server

Runs the Carl Mod Android client (`com.carlmod.game` 0.0.4, ARM64) in an Android emulator on the
Windows host and connects it to a local Carl Mod dedicated server. All scripts are PowerShell and
live in `tools\` and `tools\android\`. Everything they create goes under `.runtime\` (gitignored) or
the Android SDK / AVD folders in the user profile.

Testing ProjectMER on the Android client (fixtures, stage, tool gun) is described in
[projectmer-mobile](https://github.com/Michaelihc/projectmer-mobile).

## Requirements

- Windows Hypervisor Platform (WHPX) enabled. `emulator -accel-check` must report
  "WHPX ... is installed and usable". The scripts never change Windows features.
- Java 17 or newer on `PATH` (the SDK tools run on JDK 25).
- The server ZIP extracted to `.runtime\server-original\` and the client APK at
  `.refereces\Carl Mod - 0.0.4.apk.1`. Neither is modified; copies are used.

## One-time setup

```powershell
.\tools\android\Install-AndroidSdk.ps1
```

Installs the command-line tools, platform-tools, emulator and the
`system-images/android-36/google_apis/x86_64` image under `%LOCALAPPDATA%\Android\Sdk`, and creates
the AVD `carlmod_api36` (Pixel 6 profile, landscape, 6 GB RAM, 8 cores, host GPU, 16 GB data).
The x86_64 image runs `arm64-v8a` code through its built-in ARM translation
(`getprop ro.product.cpu.abilist` = `x86_64,arm64-v8a`).

Use the API 36 image. The ARM translator in the API 34 image does not implement the AArch64 `SEVL`
instruction (`0xd50320bf`) that `libunity.so` executes during loading, so the client aborts with
SIGILL there.

## Start the server

```powershell
.\tools\Start-TestServer.ps1 -Port 7791 -CommandSession emu `
    -LogPath "$PWD\.runtime\logs\server-emu-7791.log"
```

- Copies `.runtime\server-original\` to `.runtime\server-emu\` when missing and writes
  `hoster_policy.txt` (`gamedir_for_configs: true`). Configs, bans and logs then live in
  `.runtime\server-emu\AppData\`. The one exception is the player-prefs file
  `%APPDATA%\SCP Secret Laboratory\registry.txt`: the server binds its path before it reads the
  policy, so round restarts update the server keys `LastRoundrestartTime` and `SrvSp_*` in the real
  file (`-appdatapath` does not change this). Nothing else is written there.
- Sets `afk_time: 0` in `AppData\config\7791\config_gameplay.txt`. Without it the server kicks a
  lone client that stays in its spawn for 90 s. On the very first start the server boots once to
  generate the config, then restarts with the override. Override other keys with
  `-ConfigOverrides @{ key = 'value' }`.
- Starts `Carl Mod.exe -batchmode -nographics <console arg> -port7791 -logFile <path>` in the server
  directory, waits for `Server started listening` (about 35 s) and prints the PID. The PID is stored
  in `.runtime\pids\server-7791.pid`.
- No authentication, online-mode or whitelist changes are needed: the client joins with the default
  config (`enable_whitelist: false`; the decompiled server has no online-mode setting).

The server does not read stdin, so `-stdout` (the default without `-CommandSession`) gives a console
that cannot take commands. `-CommandSession emu` replaces `-stdout` with the file console
(`-keyemu`); commands are then sent with:

```powershell
.\tools\Send-ServerCommand.ps1 -Command forcestart      # start the round
.\tools\Send-ServerCommand.ps1 -Command roundrestart    # back to the lobby
```

The file console accepts console and remote-admin commands but does not return their text (each
output entry is written as its type name). Check effects on the client or in the server log. Every
command also logs a harmless `Error in server console: Error while sending message.`

Port 7791 only; other ports belong to other test runs.

## Start the emulator and the client

```powershell
.\tools\android\Start-Emulator.ps1               # cold boot, waits for sys.boot_completed (~1 min)
.\tools\android\Install-Client.ps1               # copies the APK to .runtime\apk\ and installs it (~25 s)
.\tools\android\Start-Client.ps1 -Restart        # launches the game
.\tools\android\Connect-Client.ps1               # joins 10.0.2.2:7791 and waits for the Facility scene
.\tools\Send-ServerCommand.ps1 -Command forcestart
```

The emulator reaches the host at `10.0.2.2`. The game data persists in the AVD, so the install is
needed only once per AVD (or when testing a rebuilt APK: `Install-Client.ps1 -Apk <path>`).
`Start-Emulator.ps1 -NoWindow` runs without a visible window.

A client that joins while a round is running becomes a spectator and sees a black screen with
spectator controls. Use `roundrestart` and then `forcestart` to spawn it.

`Start-Emulator.ps1` writes the emulator log to `.runtime\logs\emulator-<console port>.log` and marks the
Android "Viewing full screen" hint as seen; on a fresh AVD that hint otherwise covers the game's first launch and
swallows the `Connect-Client.ps1` taps.

## Two clients

Multiplayer interactions (spectating, cuffing, SCP attacks on a player) need a second client. An AVD can run only
once, so the second emulator uses its own AVD (same API 36 image) and console port. Every script in
`tools\android\` takes `-Serial emulator-<port>` (or `-ConsolePort <port>`); the default stays `emulator-5554`.

```powershell
.\tools\android\Install-AndroidSdk.ps1 -Avd carlmod_api36_b        # once: creates the second AVD
.\tools\android\Start-Emulator.ps1                                   # emulator-5554, AVD carlmod_api36
.\tools\android\Start-Emulator.ps1 -Avd carlmod_api36_b -Serial emulator-5556
.\tools\android\Install-Client.ps1 -Serial emulator-5556             # once per AVD
.\tools\android\Start-Client.ps1 -Restart -Serial emulator-5556
.\tools\android\Connect-Client.ps1 -Serial emulator-5556
.\tools\android\Capture.ps1 -Name b-view -Serial emulator-5556
.\tools\android\Stop-Emulator.ps1 -Serial emulator-5556
```

Both emulators (6 GB RAM, 8 cores each) and the server run together on the reference host. Each AVD has its own
device ID, so the clients join as different players. Player IDs follow the join order and change on every
reconnect (a client that was connected when the server restarted reconnects by itself); read them from the
`Joined` lines of the server log before addressing a player. With two players the lobby starts the round on its own
after a short countdown; `roundlock enable` (RA, `/roundlock enable` from the file console) keeps a test round
running when one side dies.

The touch HUD cannot reach every action. EventProbe (`tools\EventProbe`) has commands that position players
(`probe front`, `probe face`, `probe aimat` for SCP-049 resurrection and SCP-049-2 consumption, whose corpse must be
under the crosshair) and server-side stand-ins for actions without a touch control in the emulator (`probe cuff`,
`probe lunge`, `probe atlas`, `probe killby`); see its README.

## Driving the client UI

Screenshots: `.\tools\android\Capture.ps1 -Name <label>` writes
`.runtime\captures\<timestamp>-<label>.png` (2400x1080 landscape) and prints the path.

Input uses `adb shell input` against `emulator-5554`. Coordinates on the 2400x1080 screen:

| Step | Tap (x, y) |
| --- | --- |
| Main menu, 游戏 (Game) tab | 298, 50 |
| 游戏 tab, IP直连 (Direct connect) | 395, 348 |
| Direct connect popup, address field | 1200, 526 |
| Type the address | `adb shell input text "10.0.2.2:7791"` |
| Direct connect popup, 连接 (Connect) | 1063, 593 |
| In game, joystick centre (swipe to walk) | 358, 748 |
| In game, E (interact) | 2118, 596 |

Main menu tabs from the left: 开始, 游戏, 设置, 操作指南, Console, 退出. In-game overlay buttons:
菜单 (menu), 控制台 (console), 玩家列表 (player list), 管理 (remote admin).

Example: `adb -s emulator-5554 shell input swipe 358 748 358 480 4000` walks forward for 4 s.

SCP HUDs (same screen): the large attack button (SCP-049 attack, SCP-049-2 / SCP-106 攻击, SCP-939 claw,
SCP-173 snap) is at 1728, 786; E at 2118, 596 is the SCP-049 resurrection (hold about 10 s) and SCP-049-2
consumption. Dragging on the right half of the screen turns the camera (`input swipe 1300 300 1300 560 600` looks
down). SCP-106's stalk is the icon at 2316, 276. SCP-079: 标记 (2322, 274) toggles ping mode, then a long press on the
view pings; 地图 (72, 1008) opens the map, where tapping a room switches cameras.

Not reachable from the touch HUD in the emulator: cuffing (the fork's `DisarmingController` ignores touch mode),
SCP-939 focus and lunge (no `MobileCrouch` control on the SCP-939 HUD), SCP-106's Hunter's Atlas (the minimap renders
empty, so no room can be selected) and SCP-079's room blackout (no control found that sends `Scp079Blackout`;
keyboard keys are not read in touch mode). Use the EventProbe stand-ins for the first three.

## Measure frame rate

```powershell
.\tools\android\Measure-FrameTime.ps1 -Name <label> -DurationSec 30
```

Uses `dumpsys SurfaceFlinger --timestats`: it clears the statistics, waits, and reads the game's
SurfaceView layer (`totalFrames`, `averageFPS`, `present2present` histogram). Output: average FPS,
average frame time, P50/P95/P99 frame interval and the share of intervals above 33.4 ms. Results are
also written to `.runtime\captures\frametime-<label>-<timestamp>.json`.
(`dumpsys SurfaceFlinger --latency` returns no frame rows for this layer on Android 16, and
`dumpsys gfxinfo` does not see Unity's SurfaceView rendering.)

The client runs through ARM binary translation, so the numbers are not phone numbers; use them only
to compare builds. For a comparison:

- same AVD, resolution, host load and scene; stand still in the D-class spawn cell after `forcestart`
  (the cell geometry in view is the same every round);
- take at least three 30 s runs per build and compare the means; run-to-run spread is about +-4 fps;
- the display refreshes at 60 Hz, so frame intervals cluster at 16.7, 33.3 and 50 ms;
- interleave the configurations you compare (A, B, A) rather than measuring one after the other.

Windows applies power throttling (EcoQoS) to the emulator's qemu process as soon as another window has focus; in one
scene that cut the client from about 51 to 39 FPS. `Start-Emulator.ps1` opts the qemu process of its console port out
(also when the emulator is already running), and `Measure-FrameTime.ps1` does the same before every run, so numbers no
longer depend on which window is in front. Measurements taken without it (including the reference values below) mix
throttled and unthrottled runs.

Reference values for the 0.0.4 APK (AVD `carlmod_api36`, 2400x1080, server running on the same host,
D-class spawn cell, standing still): 31 to 39 FPS (mean of four runs 34.4 FPS), P50 interval 27 to
30 ms, P95 44 to 48 ms, P99 48 to 62 ms.

Other client facts visible in logcat: Unity reports 2 CPU cores, the ES 3.2 context request fails and
Unity falls back to a lower ES version, and the process runs as `Google sdk_gphone64_x86_64`.
`adb -s emulator-5554 logcat -s Unity:V` shows the client log.

## Stop everything

```powershell
.\tools\android\Stop-Emulator.ps1      # adb emu kill for emulator-5554 only
.\tools\android\Stop-Emulator.ps1 -Serial emulator-5556
.\tools\Stop-TestServer.ps1 -Port 7791 # stops the PID from the PID file, after checking its command line
```

Both scripts only stop the process they started and never match by image name.
