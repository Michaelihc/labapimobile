<#
.SYNOPSIS
  Measures client frame rate and frame pacing on the emulator from SurfaceFlinger TimeStats.

.DESCRIPTION
  Enables "dumpsys SurfaceFlinger --timestats", clears it, waits -DurationSec seconds, dumps the
  per-layer statistics of the game's SurfaceView layer, and reports:
    - AvgFps (frames presented / wall-clock seconds) and the layer's own averageFPS
    - frame-interval percentiles (P50/P95/P99) from the layer's present2present histogram
      (1 ms buckets up to 34 ms, 2 ms up to 50 ms, 4 ms up to 150 ms)
    - the share of frames whose interval exceeds -LongFrameMs
  The result is printed, returned as an object, and appended as JSON to
  <repo>\.runtime\captures\frametime-<Name>-<timestamp>.json.

  ("dumpsys SurfaceFlinger --latency <layer>" returns no frame rows for this layer on Android 16 and
  "dumpsys gfxinfo" does not see SurfaceView/Unity rendering, so TimeStats is the method used.)

  The client runs through ARM64 binary translation, so absolute numbers are not phone numbers. Use the
  same AVD, resolution and scene when comparing builds, and keep the player still (for example in the
  spawn cell) between runs. The display refreshes at 60 Hz, so intervals cluster at multiples of 16.7 ms.

.PARAMETER Name         Label used in the output file name.
.PARAMETER DurationSec  Sampling time (default 20).
.PARAMETER WarmupSec    Seconds to wait before sampling (default 0).
#>
[CmdletBinding()]
param(
    [string]$Name = 'run',
    [int]$DurationSec = 20,
    [int]$WarmupSec = 0,
    [int]$ConsolePort = 5554,
    [string]$Package = 'com.carlmod.game',
    [double]$LongFrameMs = 33.4
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$serial = Get-EmulatorSerial $ConsolePort
Assert-EmulatorOnline $serial
if (-not (Invoke-Adb $serial shell pidof $Package | Select-Object -First 1)) { throw "$Package is not running." }

if ($WarmupSec -gt 0) { Write-Host "Warm-up $WarmupSec s"; Start-Sleep -Seconds $WarmupSec }

Invoke-Adb $serial shell dumpsys SurfaceFlinger --timestats -enable | Out-Null
Invoke-Adb $serial shell dumpsys SurfaceFlinger --timestats -clear | Out-Null
$sw = [Diagnostics.Stopwatch]::StartNew()
Write-Host "Sampling for $DurationSec s ..."
Start-Sleep -Seconds $DurationSec
$dump = @(Invoke-Adb $serial shell dumpsys SurfaceFlinger --timestats -dump -maxlayers 30)
$elapsed = $sw.Elapsed.TotalSeconds
Invoke-Adb $serial shell dumpsys SurfaceFlinger --timestats -disable | Out-Null

# Split into per-layer blocks and pick the game's SurfaceView layer.
$text = ($dump | ForEach-Object { "$_" }) -join "`n"
$blocks = [regex]::Split($text, '(?m)^(?=layerName = )')
$block = $blocks | Where-Object { $_ -match "(?m)^layerName = .*SurfaceView\[$([regex]::Escape($Package))/" } | Select-Object -First 1
if (-not $block) { throw "No TimeStats layer for $Package. Is the game rendering? Layers seen: $(([regex]::Matches($text,'(?m)^layerName = (.*)$') | ForEach-Object { $_.Groups[1].Value }) -join '; ')" }

function Get-Num([string]$Block, [string]$Key) {
    $m = [regex]::Match($Block, "(?m)^$Key = ([\d.]+)")
    if ($m.Success) { [double]$m.Groups[1].Value } else { $null }
}
$totalFrames = Get-Num $block 'totalFrames'
$layerAvgFps = Get-Num $block 'averageFPS'
$hm = [regex]::Match($block, '(?m)^present2present histogram is as below:\s*\n(.+)$')
if (-not $hm.Success) { throw 'present2present histogram not found in TimeStats output.' }
$buckets = [regex]::Matches($hm.Groups[1].Value, '(\d+)ms=(\d+)') | ForEach-Object {
    [pscustomobject]@{ Ms = [int]$_.Groups[1].Value; Count = [int]$_.Groups[2].Value }
}
$n = ($buckets | Measure-Object Count -Sum).Sum
if ($n -lt 20) { throw "Only $n frames in the histogram; is the game rendering (not paused/backgrounded)?" }

function Get-HistPercentile($Buckets, [int]$Total, [double]$q) {
    $target = [Math]::Ceiling($q * $Total); $acc = 0
    foreach ($b in $Buckets) { $acc += $b.Count; if ($acc -ge $target) { return $b.Ms } }
    return ($Buckets | Select-Object -Last 1).Ms
}
$over = ($buckets | Where-Object { $_.Ms -ge [Math]::Ceiling($LongFrameMs) } | Measure-Object Count -Sum).Sum
$res = [ordered]@{
    Name           = $Name
    DurationSec    = [Math]::Round($elapsed, 1)
    Frames         = [int]$totalFrames
    AvgFps         = [Math]::Round($totalFrames / $elapsed, 1)
    LayerAvgFps    = $layerAvgFps
    AvgFrameMs     = [Math]::Round(1000 * $elapsed / $totalFrames, 2)
    P50Ms          = Get-HistPercentile $buckets $n 0.50
    P95Ms          = Get-HistPercentile $buckets $n 0.95
    P99Ms          = Get-HistPercentile $buckets $n 0.99
    FramesOverLong = [int]$over
    PctOverLong    = [Math]::Round(100.0 * $over / $n, 1)
    LongFrameMs    = $LongFrameMs
    Histogram      = ($buckets | Where-Object { $_.Count -gt 0 } | ForEach-Object { "$($_.Ms)ms=$($_.Count)" }) -join ' '
}
$capDir = Join-Path $script:Repo '.runtime\captures'
New-Item -ItemType Directory -Force $capDir | Out-Null
$json = Join-Path $capDir ('frametime-{0}-{1:yyyyMMdd-HHmmss}.json' -f $Name, (Get-Date))
[pscustomobject]$res | ConvertTo-Json | Set-Content $json -Encoding UTF8
[pscustomobject]$res | Format-List | Out-Host
Write-Host "Saved $json"
[pscustomobject]$res
