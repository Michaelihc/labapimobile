<#
.SYNOPSIS
  Shuts down the test emulator (graceful "adb emu kill") without touching other emulators.

.PARAMETER ConsolePort  Emulator console port (default 5554).
.PARAMETER Serial       adb serial, for example emulator-5556 (overrides -ConsolePort).
#>
[CmdletBinding()]
param(
    [int]$ConsolePort = 5554,
    [string]$Serial
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$adb = Get-Adb
$serial = Resolve-EmulatorSerial $Serial $ConsolePort
if (-not ((& $adb devices) -match "^$serial\s")) { Write-Host "$serial is not running."; return }
& $adb -s $serial emu kill | Out-Null
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline -and ((& $adb devices) -match "^$serial\s")) { Start-Sleep -Milliseconds 500 }
if ((& $adb devices) -match "^$serial\s") { throw "$serial did not shut down within 60 s." }
Write-Host "Stopped $serial."
