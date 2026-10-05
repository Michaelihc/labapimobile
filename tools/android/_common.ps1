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

# Returns the adb serial of the target emulator: -Serial when given (for example emulator-5556), otherwise
# emulator-<ConsolePort>. Every script takes both, so a second emulator is addressed with either one.
function Resolve-EmulatorSerial([string]$Serial, [int]$ConsolePort = 5554) {
    if ($Serial) { return $Serial }
    return Get-EmulatorSerial $ConsolePort
}

# Console port of an emulator-<port> serial.
function Get-EmulatorConsolePort([string]$Serial) {
    if ($Serial -notmatch '^emulator-(\d+)$') { throw "'$Serial' is not an emulator serial (emulator-<console port>)." }
    return [int]$Matches[1]
}

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

# Windows applies power throttling (EcoQoS: efficiency cores, lower clocks) to processes whose windows are in the
# background, and the emulator's qemu process qualifies as soon as another window has focus. On the reference host
# that cut the client from about 51 to 39 FPS in the same scene. This opts the qemu process of the given console port
# out of execution-speed and timer-resolution throttling (PROCESS_POWER_THROTTLING_STATE with an empty StateMask) and
# returns its PID, or $null when no such process runs. Only the emulator started for that port is touched.
function Disable-EmulatorPowerThrottling([int]$ConsolePort) {
    $qemu = Get-CimInstance Win32_Process -Filter "Name='qemu-system-x86_64.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match "-port\s+$ConsolePort(\s|$)" } | Select-Object -First 1
    if (-not $qemu) { return $null }
    if (-not ('LabApiMobile.PowerThrottling' -as [type])) {
        Add-Type -Namespace LabApiMobile -Name PowerThrottling -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct State { public uint Version; public uint ControlMask; public uint StateMask; }
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
public static extern System.IntPtr OpenProcess(uint access, bool inherit, int pid);
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
public static extern bool SetProcessInformation(System.IntPtr process, int infoClass, ref State info, int size);
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern bool CloseHandle(System.IntPtr handle);
'@
    }
    # PROCESS_SET_INFORMATION; ProcessPowerThrottling = 4; EXECUTION_SPEED (1) | IGNORE_TIMER_RESOLUTION (4), state off.
    $handle = [LabApiMobile.PowerThrottling]::OpenProcess(0x0200, $false, [int]$qemu.ProcessId)
    if ($handle -eq [IntPtr]::Zero) { Write-Warning "Cannot open qemu PID $($qemu.ProcessId) to disable power throttling."; return $null }
    try {
        $state = New-Object LabApiMobile.PowerThrottling+State
        $state.Version = 1; $state.ControlMask = 5; $state.StateMask = 0
        if (-not [LabApiMobile.PowerThrottling]::SetProcessInformation($handle, 4, [ref]$state, 12)) {
            Write-Warning "SetProcessInformation failed for qemu PID $($qemu.ProcessId)."
            return $null
        }
    } finally { [void][LabApiMobile.PowerThrottling]::CloseHandle($handle) }
    return [int]$qemu.ProcessId
}
