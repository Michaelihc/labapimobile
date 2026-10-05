<#
.SYNOPSIS
  Launches (or force-restarts) the Carl Mod client on the emulator.

.PARAMETER Restart  Force-stop the app first so it starts from the main menu.
.PARAMETER Serial  adb serial of the emulator, for example emulator-5556 (overrides -ConsolePort, default emulator-5554).
#>
[CmdletBinding()]
param(
    [switch]$Restart,
    [int]$ConsolePort = 5554,
    [string]$Serial,
    [string]$Package = 'com.carlmod.game'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$serial = Resolve-EmulatorSerial $Serial $ConsolePort
Assert-EmulatorOnline $serial

if ($Restart) {
    Invoke-Adb $serial shell am force-stop $Package | Out-Null
    Invoke-Adb $serial logcat -c | Out-Null
}
Invoke-Adb $serial shell input keyevent KEYCODE_WAKEUP | Out-Null
Invoke-Adb $serial shell monkey -p $Package -c android.intent.category.LAUNCHER 1 | Out-Null
Start-Sleep -Seconds 3
$pid_ = (Invoke-Adb $serial shell pidof $Package | Select-Object -First 1)
Write-Host "Launched $Package (pid $pid_)"
