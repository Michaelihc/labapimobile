<#
.SYNOPSIS
  Installs the Carl Mod Android client (com.carlmod.game) into the running emulator.

.DESCRIPTION
  Copies the APK from .refereces\ into .runtime\apk\ when needed (the original is never modified or
  installed in place), then runs "adb install -r -g". Use -Apk to install a different build, for
  example a rebuilt client.

.PARAMETER Apk          APK to install (default <repo>\.runtime\apk\carlmod-0.0.4.apk).
.PARAMETER Reinstall    Uninstall the package first (clears app data).
.PARAMETER ConsolePort  Emulator console port (default 5554).
#>
[CmdletBinding()]
param(
    [string]$Apk,
    [switch]$Reinstall,
    [int]$ConsolePort = 5554,
    [string]$Package = 'com.carlmod.game'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$serial = Get-EmulatorSerial $ConsolePort
Assert-EmulatorOnline $serial

if (-not $Apk) {
    $Apk = Join-Path $script:Repo '.runtime\apk\carlmod-0.0.4.apk'
    if (-not (Test-Path -LiteralPath $Apk)) {
        $orig = Join-Path $script:Repo '.refereces\Carl Mod - 0.0.4.apk.1'
        New-Item -ItemType Directory -Force (Split-Path -Parent $Apk) | Out-Null
        Copy-Item -LiteralPath $orig -Destination $Apk
    }
}
if (-not (Test-Path -LiteralPath $Apk)) { throw "APK not found: $Apk" }

if ($Reinstall) { Invoke-Adb $serial uninstall $Package | Out-Host }

$sw = [Diagnostics.Stopwatch]::StartNew()
$out = Invoke-Adb $serial install -r -g $Apk
$out | Out-Host
if (($out | Out-String) -notmatch 'Success') { throw "adb install failed." }
Write-Host ("Installed {0} in {1:N0} s" -f $Package, $sw.Elapsed.TotalSeconds)
Invoke-Adb $serial shell dumpsys package $Package | Select-String 'versionName|versionCode|primaryCpuAbi' | Select-Object -First 3 | Out-Host
