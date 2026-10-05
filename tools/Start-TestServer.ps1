<#
.SYNOPSIS
  Starts a local Carl Mod dedicated server for client testing and waits until it is listening.

.DESCRIPTION
  - Creates the server directory from -SourceDir when it does not exist yet.
  - Writes hoster_policy.txt (gamedir_for_configs: true) when missing, so configs and logs go to
    <ServerDir>\AppData instead of the real %APPDATA%\SCP Secret Laboratory.
  - Refuses to start when the UDP port is already in use.
  - Starts "Carl Mod.exe" with the working directory set to the server directory. Argument order
    matters: -stdout must come before -port<N>.
  - Waits for "Server started listening" in the Unity log, then prints the PID and writes it to
    <repo>\.runtime\pids\server-<port>.pid so Stop-TestServer.ps1 can stop exactly this process.

  Console commands: the server does not read stdin. Use -CommandSession to start the file-based
  console instead of -stdout, then send commands with tools\Send-ServerCommand.ps1.

.PARAMETER ServerDir      Server directory (default: <repo>\.runtime\server-emu).
.PARAMETER SourceDir      Pristine server copied to ServerDir when ServerDir is missing.
.PARAMETER Port           Game port (default 7791).
.PARAMETER LogPath        Unity -logFile path (default: <repo>\.runtime\logs\server-<port>-<timestamp>.log).
.PARAMETER TimeoutSec     Seconds to wait for readiness (default 180).
.PARAMETER CommandSession File-console session name. When set, "-key<name>" replaces "-stdout".
.PARAMETER ExtraArgs      Additional server arguments appended after -port.
#>
[CmdletBinding()]
param(
    [string]$ServerDir,
    [string]$SourceDir,
    [int]$Port = 7791,
    [string]$LogPath,
    [int]$TimeoutSec = 180,
    [string]$CommandSession,
    [string[]]$ExtraArgs = @()
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ServerDir) { $ServerDir = Join-Path $repo '.runtime\server-emu' }
if (-not $SourceDir) { $SourceDir = Join-Path $repo '.runtime\server-original' }
if (-not $LogPath)   { $LogPath = Join-Path $repo ('.runtime\logs\server-{0}-{1:yyyyMMdd-HHmmss}.log' -f $Port, (Get-Date)) }
$ServerDir = [IO.Path]::GetFullPath($ServerDir)
$LogPath   = [IO.Path]::GetFullPath($LogPath)
$exe = Join-Path $ServerDir 'Carl Mod.exe'

if (-not (Test-Path -LiteralPath $exe)) {
    if (-not (Test-Path -LiteralPath (Join-Path $SourceDir 'Carl Mod.exe'))) {
        throw "Server not found at '$ServerDir' and no pristine copy at '$SourceDir'."
    }
    Write-Host "Copying '$SourceDir' to '$ServerDir'"
    robocopy $SourceDir $ServerDir /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
}

$policy = Join-Path $ServerDir 'hoster_policy.txt'
if (-not (Test-Path -LiteralPath $policy)) {
    Set-Content -LiteralPath $policy -Value 'gamedir_for_configs: true' -Encoding ASCII
    Write-Host "Wrote $policy"
}
if ((Get-Content -LiteralPath $policy -Raw) -notmatch 'gamedir_for_configs:\s*true') {
    throw "$policy does not contain 'gamedir_for_configs: true'; refusing to start (configs would go to the real %APPDATA%)."
}

if (Get-NetUDPEndpoint -LocalPort $Port -ErrorAction SilentlyContinue) {
    throw "UDP port $Port is already in use."
}

New-Item -ItemType Directory -Force (Split-Path -Parent $LogPath) | Out-Null
$pidDir = Join-Path $repo '.runtime\pids'
New-Item -ItemType Directory -Force $pidDir | Out-Null
$pidFile = Join-Path $pidDir "server-$Port.pid"
$stdoutPath = "$LogPath.stdout.txt"
if (Test-Path -LiteralPath $LogPath) { Remove-Item -LiteralPath $LogPath -Force }

$outputArg = if ($CommandSession) { "-key$CommandSession" } else { '-stdout' }
$argList = @('-batchmode', '-nographics', $outputArg, "-port$Port", '-logFile', $LogPath) + $ExtraArgs

$startParams = @{
    FilePath         = $exe
    ArgumentList     = $argList
    WorkingDirectory = $ServerDir
    PassThru         = $true
    WindowStyle      = 'Hidden'
}
if (-not $CommandSession) { $startParams.RedirectStandardOutput = $stdoutPath }
$proc = Start-Process @startParams
Set-Content -LiteralPath $pidFile -Value $proc.Id -Encoding ASCII
Write-Host "Started PID $($proc.Id): $($argList -join ' ')"

function Test-LogContains([string]$Path, [string]$Pattern) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    try {
        $fs = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
        try { $text = (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
        return $text.Contains($Pattern)
    } catch { return $false }
}

$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    if ($proc.HasExited) { throw "Server exited early with code $($proc.ExitCode). See $LogPath" }
    if (Test-LogContains $LogPath 'Server started listening') {
        Write-Host "Server ready on UDP port $Port (PID $($proc.Id))"
        Write-Host "Log:      $LogPath"
        Write-Host "AppData:  $(Join-Path $ServerDir 'AppData')"
        Write-Host "PID file: $pidFile"
        $proc.Id
        return
    }
    Start-Sleep -Milliseconds 500
}
throw "Timed out after $TimeoutSec s waiting for 'Server started listening'. See $LogPath (PID $($proc.Id) still running)."
