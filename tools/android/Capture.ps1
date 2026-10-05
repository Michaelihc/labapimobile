<#
.SYNOPSIS
  Saves an emulator screenshot (PNG) and prints its path.

.EXAMPLE
  .\Capture.ps1 -Name main-menu
  Writes <repo>\.runtime\captures\<timestamp>-main-menu.png
#>
[CmdletBinding()]
param(
    [string]$Name = 'shot',
    [string]$OutDir,
    [int]$ConsolePort = 5554
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$serial = Get-EmulatorSerial $ConsolePort
Assert-EmulatorOnline $serial
if (-not $OutDir) { $OutDir = Join-Path $script:Repo '.runtime\captures' }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$file = Join-Path $OutDir ('{0:yyyyMMdd-HHmmss}-{1}.png' -f (Get-Date), $Name)

$code = Save-AdbBinary -Serial $serial -OutFile $file -AdbArgs @('exec-out', 'screencap', '-p')
if ($code -ne 0 -or (Get-Item $file).Length -lt 1000) { throw "screencap failed (exit $code)." }
$file
