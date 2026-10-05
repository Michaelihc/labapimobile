<#
.SYNOPSIS
  Installs the Android SDK pieces and creates the Carl Mod test AVD. Idempotent.

.DESCRIPTION
  - Downloads the official command-line tools zip from dl.google.com into <Sdk>\cmdline-tools\latest.
  - Installs platform-tools, emulator and the Google APIs x86_64 system image (API 36 / Android 16). That image
    runs arm64-v8a apps through its built-in ARM translation.
  - Creates the AVD (Pixel 6 profile, landscape, 6 GB RAM, 8 cores, host GPU, 16 GB data partition).
  The Windows Hypervisor Platform (WHPX) must already be enabled; this script does not change
  Windows features. "emulator -accel-check" is run at the end.

.PARAMETER Sdk      SDK root (default %LOCALAPPDATA%\Android\Sdk).
.PARAMETER AvdName  AVD name (default carlmod_api36).
.PARAMETER Recreate Delete and recreate the AVD.
#>
[CmdletBinding()]
param(
    [string]$Sdk = (Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
    [string]$AvdName = 'carlmod_api36',
    [string]$CmdlineToolsUrl = 'https://dl.google.com/android/repository/commandlinetools-win-16111833_latest.zip',
    [string]$SystemImage = 'system-images/android-36/google_apis/x86_64',
    [switch]$Recreate
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$env:ANDROID_SDK_ROOT = $Sdk
$sdkmanager = Join-Path $Sdk 'cmdline-tools\latest\bin\sdkmanager.bat'
$avdmanager = Join-Path $Sdk 'cmdline-tools\latest\bin\avdmanager.bat'

if (-not (Test-Path -LiteralPath $sdkmanager)) {
    $zip = Join-Path $env:TEMP 'android-cmdline-tools.zip'
    $tmp = Join-Path $Sdk 'cmdline-tools\_extract'
    New-Item -ItemType Directory -Force $tmp | Out-Null
    Write-Host "Downloading $CmdlineToolsUrl"
    Invoke-WebRequest -UseBasicParsing $CmdlineToolsUrl -OutFile $zip
    Expand-Archive $zip -DestinationPath $tmp -Force
    Move-Item (Join-Path $tmp 'cmdline-tools') (Join-Path $Sdk 'cmdline-tools\latest')
    Remove-Item $tmp -Recurse -Force
    Remove-Item $zip -Force
}

# The current sdkmanager (Android CLI wrapper) expects slash-separated package ids for image packages.
$packages = @('platform-tools', 'emulator', $SystemImage)
& $sdkmanager --sdk_root="$Sdk" @packages 2>&1 | Where-Object { $_ -notmatch '^\s*(\.\.\.|Unzipping)?.*\[[#. ]*\]' }

$avdHome = Join-Path $env:USERPROFILE '.android\avd'
$avdDir = Join-Path $avdHome "$AvdName.avd"
if ($Recreate -and (Test-Path $avdDir)) {
    Remove-Item $avdDir -Recurse -Force
    Remove-Item (Join-Path $avdHome "$AvdName.ini") -Force -ErrorAction SilentlyContinue
}
if (-not (Test-Path (Join-Path $avdDir 'config.ini'))) {
    $imageId = $SystemImage -replace '/', ';'
    'no' | & $avdmanager create avd -n $AvdName -k $imageId -d pixel_6 --force 2>&1 | Out-Null
    $cfg = Join-Path $avdDir 'config.ini'
    if (-not (Test-Path $cfg)) { throw "avdmanager did not create $cfg" }
    $set = [ordered]@{
        'avd.id' = $AvdName; 'avd.name' = $AvdName
        'hw.ramSize' = '6144'; 'hw.cpu.ncore' = '8'; 'vm.heapSize' = '512M'
        'hw.gpu.enabled' = 'yes'; 'hw.gpu.mode' = 'host'
        'hw.initialOrientation' = 'landscape'; 'hw.keyboard' = 'yes'
        'disk.dataPartition.size' = '16G'; 'showDeviceFrame' = 'no'
    }
    $lines = foreach ($l in (Get-Content $cfg)) {
        $k = ($l -split '=', 2)[0]
        if ($k -eq 'disk.dataPartition.path') { continue }   # "<temp>" would discard the installed APK on every boot
        if ($set.Contains($k)) { "$k=$($set[$k])"; $set.Remove($k) } else { $l }
    }
    $lines += $set.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }
    Set-Content $cfg $lines -Encoding ASCII
    Write-Host "Created AVD $AvdName"
} else {
    Write-Host "AVD $AvdName already exists"
}

& (Join-Path $Sdk 'emulator\emulator.exe') -accel-check
