# Testing the Android client against a local server

Runs the Carl Mod Android client (`com.carlmod.game` 0.0.4, ARM64) in an Android emulator on the
Windows host and connects it to a local Carl Mod dedicated server. All scripts are PowerShell and
live in `tools\` and `tools\android\`. Everything they create goes under `.runtime\` (gitignored) or
the Android SDK / AVD folders in the user profile.

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
- the display refreshes at 60 Hz, so frame intervals cluster at 16.7, 33.3 and 50 ms.

Reference values for the 0.0.4 APK (AVD `carlmod_api36`, 2400x1080, server running on the same host,
D-class spawn cell, standing still): 31 to 39 FPS (mean of four runs 34.4 FPS), P50 interval 27 to
30 ms, P95 44 to 48 ms, P99 48 to 62 ms.

Other client facts visible in logcat: Unity reports 2 CPU cores, the ES 3.2 context request fails and
Unity falls back to a lower ES version, and the process runs as `Google sdk_gphone64_x86_64`.
`adb -s emulator-5554 logcat -s Unity:V` shows the client log.

## Stop everything

```powershell
.\tools\android\Stop-Emulator.ps1      # adb emu kill for emulator-5554 only
.\tools\Stop-TestServer.ps1 -Port 7791 # stops the PID from the PID file, after checking its command line
```

Both scripts only stop the process they started and never match by image name.
