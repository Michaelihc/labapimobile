<#
.SYNOPSIS
  Boots the Carl Mod test AVD with WHPX acceleration and waits until Android has finished booting.

.DESCRIPTION
  Cold boots by default (no snapshot) so every run starts from the same state. The emulator log goes
  to <repo>\.runtime\logs\emulator.log. Stop it with Stop-Emulator.ps1.

.PARAMETER AvdName      AVD to boot (default carlmod_api36).
.PARAMETER ConsolePort  Emulator console port; adb serial is emulator-<port> (default 5554).
.PARAMETER NoWindow     Run without a visible window (rendering still uses the host GPU).
.PARAMETER Snapshot     Allow snapshot load/save instead of a cold boot.
#>
[CmdletBinding()]
param(
    [string]$AvdName = 'carlmod_api36',
    [int]$ConsolePort = 5554,
    [switch]$NoWindow,
    [switch]$Snapshot,
    [int]$TimeoutSec = 300
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$sdk = Get-AndroidSdk
$adb = Get-Adb
$serial = Get-EmulatorSerial $ConsolePort
$env:ANDROID_SDK_ROOT = $sdk
$emulator = Join-Path $sdk 'emulator\emulator.exe'

if ((& $adb devices) -match "^$serial\s+device") {
    Write-Host "$serial is already running."
    return
}

$logDir = Join-Path $script:Repo '.runtime\logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$log = Join-Path $logDir 'emulator.log'

$emuArgs = @('-avd', $AvdName, '-port', $ConsolePort, '-accel', 'on', '-gpu', 'host',
             '-no-boot-anim', '-no-audio', '-netdelay', 'none', '-netspeed', 'full')
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
& $adb -s $serial shell input keyevent KEYCODE_WAKEUP | Out-Null
& $adb -s $serial shell wm dismiss-keyguard | Out-Null

$abis = (& $adb -s $serial shell getprop ro.product.cpu.abilist).Trim()
Write-Host "Booted $serial. Android $((& $adb -s $serial shell getprop ro.build.version.release).Trim()), ABIs: $abis"
if ($abis -notmatch 'arm64-v8a') { Write-Warning 'arm64-v8a is not in the ABI list; the ARM64 APK will not run.' }
