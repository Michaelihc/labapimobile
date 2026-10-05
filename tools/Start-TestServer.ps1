<#
.SYNOPSIS
  Starts a local Carl Mod dedicated server for client testing and waits until it is listening.

.DESCRIPTION
  - Creates the server directory from -SourceDir when it does not exist yet.
  - Writes hoster_policy.txt (gamedir_for_configs: true) when missing, so configs and logs go to
    <ServerDir>\AppData instead of the real %APPDATA%\SCP Secret Laboratory. One file still goes to
    the real folder: the server's player-prefs file registry.txt (it stores the server's
    LastRoundrestartTime and SrvSp_* values). The server binds that path before it reads the
    policy, and neither hoster_policy.txt nor -appdatapath changes it.
  - Applies -ConfigOverrides to AppData\config\<port>\config_gameplay.txt. The default disables the
    AFK kick (afk_time: 0): a lone test client standing in its spawn is otherwise kicked after 90 s.
    On the very first start the config file does not exist yet, so the server is booted once to
    generate it, stopped, patched and started again.
  - Refuses to start when the UDP port is already in use.
  - Starts "Carl Mod.exe" with the working directory set to the server directory. Argument order
    matters: -stdout must come before -port<N>.
  - Waits for "Server started listening" in the Unity log, then prints the PID and writes it to
    <repo>\.runtime\pids\server-<port>.pid so Stop-TestServer.ps1 can stop exactly this process.

  Console commands: the server does not read stdin. Use -CommandSession to start the file-based
  console instead of -stdout, then send commands with tools\Send-ServerCommand.ps1.

.PARAMETER ServerDir       Server directory (default: <repo>\.runtime\server-emu).
.PARAMETER SourceDir       Pristine server copied to ServerDir when ServerDir is missing.
.PARAMETER Port            Game port (default 7791).
.PARAMETER LogPath         Unity -logFile path (default: <repo>\.runtime\logs\server-<port>-<timestamp>.log).
.PARAMETER TimeoutSec      Seconds to wait for readiness (default 180).
.PARAMETER CommandSession  File-console session name. When set, "-key<name>" replaces "-stdout".
.PARAMETER ConfigOverrides config_gameplay.txt keys to set (default @{ afk_time = '0' }). Pass @{} for none.
.PARAMETER ExtraArgs       Additional server arguments appended after -port.
#>
[CmdletBinding()]
param(
    [string]$ServerDir,
    [string]$SourceDir,
    [int]$Port = 7791,
    [string]$LogPath,
    [int]$TimeoutSec = 180,
    [string]$CommandSession,
    [hashtable]$ConfigOverrides = @{ afk_time = '0' },
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

$pidDir = Join-Path $repo '.runtime\pids'
New-Item -ItemType Directory -Force $pidDir | Out-Null
New-Item -ItemType Directory -Force (Split-Path -Parent $LogPath) | Out-Null
$pidFile = Join-Path $pidDir "server-$Port.pid"
$configFile = Join-Path $ServerDir "AppData\config\$Port\config_gameplay.txt"

function Test-LogContains([string]$Path, [string]$Pattern) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    try {
        $fs = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
        try { $text = (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
        return $text.Contains($Pattern)
    } catch { return $false }
}

# Starts the server and waits for readiness. Returns the Process.
function Start-ServerProcess([string]$Log, [bool]$UseFileConsole) {
    if (Test-Path -LiteralPath $Log) { Remove-Item -LiteralPath $Log -Force }
    if ($UseFileConsole) {
        # FileConsole creates a FileSystemWatcher on this directory in its constructor, so it must exist.
        $sessionDir = Join-Path $ServerDir "Carl Mod_Data\Dedicated\$CommandSession"
        if (Test-Path -LiteralPath $sessionDir) { Remove-Item -LiteralPath $sessionDir -Recurse -Force }
        New-Item -ItemType Directory -Force $sessionDir | Out-Null
    }
    $outputArg = if ($UseFileConsole) { "-key$CommandSession" } else { '-stdout' }
    $argList = @('-batchmode', '-nographics', $outputArg, "-port$Port", '-logFile', $Log) + $ExtraArgs
    $startParams = @{
        FilePath = $exe; ArgumentList = $argList; WorkingDirectory = $ServerDir
        PassThru = $true; WindowStyle = 'Hidden'
    }
    if (-not $UseFileConsole) { $startParams.RedirectStandardOutput = "$Log.stdout.txt" }
    $p = Start-Process @startParams
    Set-Content -LiteralPath $pidFile -Value $p.Id -Encoding ASCII
    Write-Host "Started PID $($p.Id): $($argList -join ' ')"

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($p.HasExited) { throw "Server exited early with code $($p.ExitCode). See $Log" }
        if (Test-LogContains $Log 'Server started listening') { return $p }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out after $TimeoutSec s waiting for 'Server started listening'. See $Log (PID $($p.Id) still running)."
}

function Set-ConfigValues([string]$File, [hashtable]$Values) {
    $lines = [Collections.Generic.List[string]](Get-Content -LiteralPath $File)
    foreach ($key in $Values.Keys) {
        $new = "${key}: $($Values[$key])"
        $idx = -1
        for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^$([regex]::Escape($key)):") { $idx = $i; break } }
        if ($idx -ge 0) { $lines[$idx] = $new } else { $lines.Add($new) }
    }
    Set-Content -LiteralPath $File -Value $lines -Encoding UTF8
    Write-Host "Config $File : $(($Values.Keys | ForEach-Object { "$_=$($Values[$_])" }) -join ', ')"
}

if ($ConfigOverrides.Count -gt 0 -and -not (Test-Path -LiteralPath $configFile)) {
    Write-Host 'First start: booting once to generate the config files.'
    $boot = Start-ServerProcess "$LogPath.bootstrap.log" $false
    Stop-Process -Id $boot.Id -Force
    $boot.WaitForExit()
    Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    if (-not (Test-Path -LiteralPath $configFile)) { throw "Server did not create $configFile." }
}
if ($ConfigOverrides.Count -gt 0) { Set-ConfigValues $configFile $ConfigOverrides }

$proc = Start-ServerProcess $LogPath ([bool]$CommandSession)
Write-Host "Server ready on UDP port $Port (PID $($proc.Id))"
Write-Host "Log:      $LogPath"
Write-Host "AppData:  $(Join-Path $ServerDir 'AppData')"
Write-Host "PID file: $pidFile"
$proc.Id
