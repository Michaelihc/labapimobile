# Shared helpers for tools\android\*.ps1 (dot-source this file).

$script:Repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Get-AndroidSdk {
    if ($env:ANDROID_SDK_ROOT -and (Test-Path $env:ANDROID_SDK_ROOT)) { return $env:ANDROID_SDK_ROOT }
    if ($env:ANDROID_HOME -and (Test-Path $env:ANDROID_HOME)) { return $env:ANDROID_HOME }
    return (Join-Path $env:LOCALAPPDATA 'Android\Sdk')
}

function Get-Adb {
    $adb = Join-Path (Get-AndroidSdk) 'platform-tools\adb.exe'
    if (-not (Test-Path -LiteralPath $adb)) { throw "adb not found at $adb. Run tools\android\Install-AndroidSdk.ps1." }
    return $adb
}

function Get-EmulatorSerial([int]$ConsolePort = 5554) { return "emulator-$ConsolePort" }

# Runs adb against the test emulator and returns the output lines. Native stderr is merged so callers
# see adb errors. Does not throw on non-zero exit; check $LASTEXITCODE when it matters.
function Invoke-Adb {
    # Simple function (no param block) so adb flags such as -p reach adb instead of PowerShell binding.
    # Usage: Invoke-Adb <serial> <adb args...>
    $Serial = $args[0]
    $AdbArgs = @(if ($args.Count -gt 1) { $args[1..($args.Count - 1)] })
    $adb = Get-Adb
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & $adb -s $Serial @AdbArgs 2>&1 } finally { $ErrorActionPreference = $prev }
}

# Saves a raw adb exec-out stdout stream to a file without PowerShell re-encoding the bytes.
function Save-AdbBinary {
    param([string]$Serial, [string]$OutFile, [string[]]$AdbArgs)
    $adb = Get-Adb
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $adb
    $psi.Arguments = (@('-s', $Serial) + $AdbArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.RedirectStandardOutput = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $p = [Diagnostics.Process]::Start($psi)
    $fs = [IO.File]::Create($OutFile)
    try { $p.StandardOutput.BaseStream.CopyTo($fs) } finally { $fs.Dispose() }
    $p.WaitForExit()
    return $p.ExitCode
}

function Assert-EmulatorOnline([string]$Serial) {
    $state = (Invoke-Adb $Serial get-state | Select-Object -First 1)
    if ("$state".Trim() -ne 'device') { throw "Emulator '$Serial' is not online (state: $state). Run tools\android\Start-Emulator.ps1." }
}
