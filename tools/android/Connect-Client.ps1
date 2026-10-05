<#
.SYNOPSIS
  Drives the Carl Mod main menu to join a server by IP and waits until the Facility scene loads.

.DESCRIPTION
  Taps through: 游戏 (Game) tab -> IP直连 (Direct connect) -> address field -> types the address ->
  连接 (Connect). Tap positions are fractions of the 2400x1080 landscape screen, so they hold for other
  landscape resolutions with the same aspect ratio. The client must be at the main menu
  (Start-Client.ps1 -Restart first). "Joined" means the Unity log reports the Facility scene; the
  round state is then up to the server (use tools\Send-ServerCommand.ps1 -Command forcestart).

.PARAMETER Address  host[:port] to join. The emulator reaches the host machine at 10.0.2.2.
.PARAMETER Serial  adb serial of the emulator, for example emulator-5556 (overrides -ConsolePort, default emulator-5554).
#>
[CmdletBinding()]
param(
    [string]$Address = '10.0.2.2:7791',
    [int]$ConsolePort = 5554,
    [string]$Serial,
    [int]$MenuTimeoutSec = 90,
    [int]$JoinTimeoutSec = 120
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$serial = Resolve-EmulatorSerial $Serial $ConsolePort
Assert-EmulatorOnline $serial
if (-not (Invoke-Adb $serial shell pidof com.carlmod.game | Select-Object -First 1)) { throw 'The client is not running. Run Start-Client.ps1 first.' }

# Landscape screen size (the physical size is reported in portrait order).
$size = (Invoke-Adb $serial shell wm size | Select-String 'Physical size: (\d+)x(\d+)')
if (-not $size) { throw 'Could not read the display size.' }
$a = [int]$size.Matches[0].Groups[1].Value; $b = [int]$size.Matches[0].Groups[2].Value
$W = [Math]::Max($a, $b); $H = [Math]::Min($a, $b)
function Tap([double]$fx, [double]$fy) { Invoke-Adb $serial shell input tap ([int]($fx * $W)) ([int]($fy * $H)) | Out-Null }

function Wait-UnityLog([string]$Pattern, [int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ((Invoke-Adb $serial logcat -d -s Unity:I | Select-String -Pattern $Pattern -Quiet)) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

if (-not (Wait-UnityLog "Loaded scene 'NewMainMenu'" $MenuTimeoutSec)) { throw "Main menu did not load within $MenuTimeoutSec s." }
Start-Sleep -Seconds 3
Invoke-Adb $serial logcat -c | Out-Null

Tap 0.1242 0.0463      # 游戏 (Game) tab
Start-Sleep -Seconds 1.5
Tap 0.1646 0.3222      # IP直连 (Direct connect)
Start-Sleep -Seconds 1.5
Tap 0.5000 0.4870      # address field
Start-Sleep -Seconds 1.5
Invoke-Adb $serial shell input text $Address | Out-Null
Start-Sleep -Seconds 1
Tap 0.4430 0.5490      # 连接 (Connect)
Write-Host "Connecting to $Address ..."

if (-not (Wait-UnityLog "Loaded scene 'Facility'" $JoinTimeoutSec)) {
    throw "Did not reach the Facility scene within $JoinTimeoutSec s. Capture a screenshot with Capture.ps1 to see the client state."
}
Write-Host 'Joined: Facility scene loaded.'
