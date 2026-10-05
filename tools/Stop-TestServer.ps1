<#
.SYNOPSIS
  Stops the test server started by Start-TestServer.ps1 for a given port.

.DESCRIPTION
  Reads <repo>\.runtime\pids\server-<port>.pid (or uses -ProcessId), checks that the process is a
  "Carl Mod.exe" whose command line contains -port<Port>, and stops only that PID. Never stops
  processes by image name.
#>
[CmdletBinding()]
param(
    [int]$Port = 7791,
    [int]$ProcessId
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$pidFile = Join-Path $repo ".runtime\pids\server-$Port.pid"

if (-not $ProcessId) {
    if (-not (Test-Path -LiteralPath $pidFile)) { Write-Host "No PID file for port $Port; nothing to stop."; return }
    $ProcessId = [int](Get-Content -LiteralPath $pidFile -Raw).Trim()
}

$info = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction SilentlyContinue
if (-not $info) {
    Write-Host "PID $ProcessId is not running."
} elseif ($info.Name -ne 'Carl Mod.exe' -or $info.CommandLine -notmatch "-port$Port(\s|$)") {
    throw "PID $ProcessId is not the port-$Port test server (name '$($info.Name)'); refusing to stop it."
} else {
    Stop-Process -Id $ProcessId -Force
    Write-Host "Stopped PID $ProcessId (port $Port)."
}
Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
