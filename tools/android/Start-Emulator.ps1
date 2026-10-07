<#
.SYNOPSIS
  Boots the Carl Mod test AVD with WHPX acceleration and waits until Android has finished booting.

.DESCRIPTION
  Cold boots by default (no snapshot) so every run starts from the same state. The emulator log goes
  to <repo>\.runtime\logs\emulator-<console port>.log. Stop it with Stop-Emulator.ps1.

  The qemu process is opted out of Windows power throttling, which otherwise slows a background
  emulator window by about a quarter (see Disable-EmulatorPowerThrottling in _common.ps1).

  An AVD can run only once, so a second emulator needs its own AVD (Install-AndroidSdk.ps1 -AvdName
  carlmod_api36_b) and console port:
    Start-Emulator.ps1 -Avd carlmod_api36_b -Serial emulator-5556

.PARAMETER AvdName      AVD to boot (default carlmod_api36). Alias -Avd.
.PARAMETER ConsolePort  Emulator console port; adb serial is emulator-<port> (default 5554).
.PARAMETER Serial       adb serial emulator-<port>; sets -ConsolePort from it.
.PARAMETER NoWindow     Run without a visible window (rendering still uses the host GPU).
.PARAMETER Snapshot     Allow snapshot load/save instead of a cold boot.
#>
[CmdletBinding()]
param(
    [Alias('Avd')]
    [string]$AvdName = 'carlmod_api36',
    [int]$ConsolePort = 5554,
    [string]$Serial,
    [switch]$NoWindow,
    [switch]$EnableAudio,
    [switch]$Snapshot,
    [int]$TimeoutSec = 300
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$sdk = Get-AndroidSdk
$adb = Get-Adb
if ($Serial) { $ConsolePort = Get-EmulatorConsolePort $Serial }
$serial = Get-EmulatorSerial $ConsolePort
$env:ANDROID_SDK_ROOT = $sdk
$emulator = Join-Path $sdk 'emulator\emulator.exe'

if ((& $adb devices) -match "^$serial\s+device") {
    Write-Host "$serial is already running."
    if (Disable-EmulatorPowerThrottling $ConsolePort) { Write-Host 'Power throttling disabled for its qemu process.' }
    return
}

$logDir = Join-Path $script:Repo '.runtime\logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$log = Join-Path $logDir "emulator-$ConsolePort.log"

$emuArgs = @('-avd', $AvdName, '-port', $ConsolePort, '-accel', 'on', '-gpu', 'host',
             '-no-boot-anim', '-netdelay', 'none', '-netspeed', 'full')
if (-not $EnableAudio) { $emuArgs += '-no-audio' }
if (-not $Snapshot) { $emuArgs += '-no-snapshot' }
if ($NoWindow) { $emuArgs += '-no-window' }

$proc = Start-Process -FilePath $emulator -ArgumentList $emuArgs -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err"
Write-Host "Started emulator launcher PID $($proc.Id) ($($emuArgs -join ' '))"

$deadline = (Get-Date).AddSeconds($TimeoutSec)
$booted = $false
while ((Get-Date) -lt $deadline) {
    if ($proc.HasExited) { throw "Emulator exited early (code $($proc.ExitCode)). See $log and $log.err" }
    $state = (& $adb -s $serial get-state 2>$null | Select-Object -First 1)
    if ("$state".Trim() -eq 'device') {
        $b = (& $adb -s $serial shell getprop sys.boot_completed 2>$null | Select-Object -First 1)
        if ("$b".Trim() -eq '1') { $booted = $true; break }
    }
    Start-Sleep -Seconds 2
}
if (-not $booted) { throw "Timed out after $TimeoutSec s waiting for boot. See $log" }

# Stay awake and keep the screen on while plugged in; skip animations for steadier input timing.
& $adb -s $serial shell svc power stayon true | Out-Null
& $adb -s $serial shell settings put global window_animation_scale 0 | Out-Null
& $adb -s $serial shell settings put global transition_animation_scale 0 | Out-Null
& $adb -s $serial shell settings put global animator_duration_scale 0 | Out-Null
# A fresh AVD shows a "Viewing full screen" hint over the game's first launch, which blocks Connect-Client.ps1 taps.
& $adb -s $serial shell settings put secure immersive_mode_confirmations confirmed | Out-Null
& $adb -s $serial shell input keyevent KEYCODE_WAKEUP | Out-Null
& $adb -s $serial shell wm dismiss-keyguard | Out-Null

# Keep the emulator at full speed when its window is not in the foreground (see _common.ps1).
$qemuPid = Disable-EmulatorPowerThrottling $ConsolePort
if ($qemuPid) { Write-Host "Power throttling disabled for qemu PID $qemuPid." }

$abis = (& $adb -s $serial shell getprop ro.product.cpu.abilist).Trim()
Write-Host "Booted $serial. Android $((& $adb -s $serial shell getprop ro.build.version.release).Trim()), ABIs: $abis"
if ($abis -notmatch 'arm64-v8a') { Write-Warning 'arm64-v8a is not in the ABI list; the ARM64 APK will not run.' }
