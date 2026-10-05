<#
.SYNOPSIS
  Sends a console command to a test server started with Start-TestServer.ps1 -CommandSession <name>.

.DESCRIPTION
  The server's file console watches <ServerDir>\Carl Mod_Data\Dedicated\<Session>\ for new files named
  cs*.mapi (content = command line) and writes one sl*.mapi file per console output entry into the
  same directory. This script moves a command file in (atomically, so the watcher never sees a
  partial file), waits, then deletes the processed command file and the output entries.

  Limits of this channel: the server never reads stdin, and the file console writes only the entry
  type name ("ServerOutput.TextOutputEntry") instead of the text, so command output cannot be read
  back. Verify effects through the client or the server log (-LogPath of Start-TestServer.ps1).
  The server also logs a harmless "Error while sending message" for every command (it cannot delete
  the file it is still reading); the command is executed regardless.

.EXAMPLE
  .\tools\Send-ServerCommand.ps1 -Command forcestart
  .\tools\Send-ServerCommand.ps1 -Command roundrestart
  .\tools\Send-ServerCommand.ps1            # only clean up accumulated output entries
#>
[CmdletBinding()]
param(
    [string]$Command,
    [string]$ServerDir,
    [string]$Session = 'emu',
    [double]$WaitSec = 1.5
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ServerDir) { $ServerDir = Join-Path $repo '.runtime\server-emu' }
$ServerDir = [IO.Path]::GetFullPath($ServerDir)
$dir = Join-Path $ServerDir "Carl Mod_Data\Dedicated\$Session"
if (-not (Test-Path -LiteralPath $dir)) {
    throw "Session directory not found: $dir. Start the server with Start-TestServer.ps1 -CommandSession $Session."
}

if ($Command) {
    $name = 'cs{0}.mapi' -f [DateTime]::UtcNow.Ticks
    $staging = Join-Path $ServerDir $name
    [IO.File]::WriteAllText($staging, $Command)
    Move-Item -LiteralPath $staging -Destination (Join-Path $dir $name)
    Write-Host "Sent: $Command"
    Start-Sleep -Milliseconds ([int]($WaitSec * 1000))
}

$out = @(Get-ChildItem -LiteralPath $dir -Filter 'sl*.mapi' -ErrorAction SilentlyContinue)
$out | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $dir -Filter 'cs*.mapi' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
if ($Command) { Write-Host "Server produced $($out.Count) output entries (text is not available through the file console)." }
