<#
.SYNOPSIS
  Builds LabApiMobile.sln in Release and writes the release archive dist\LabApiMobile-<version>.zip.

.DESCRIPTION
  The archive has one top folder, LabApiMobile-<version>\, containing:
    LabApiMobile.Installer.exe, .exe.config, Mono.Cecil.dll   installer (.NET Framework 4.6.2)
    framework\LabApi.dll, LabApi.pdb, 0Harmony.dll            installed into Carl Mod_Data\Managed
    plugins\ProjectMER.dll, ProjectMER.pdb                     copied by the server owner
    INSTALL.txt, NOTICE.txt, licenses\*.txt                    from tools\package\

  ProjectMER needs no other files: Newtonsoft.Json and YamlDotNet ship with the game, and LabApi.dll and
  0Harmony.dll come from the framework folder.

  The build needs the extracted reference server (.runtime\server-original, see tools\extract-server.py),
  because the projects compile against its Managed folder.

.PARAMETER Version        Package version (default: <Version> of src\LabApi\LabApi.csproj).
.PARAMETER OutputDir      Where the zip and the staging folder go (default: <repo>\dist).
.PARAMETER ArtifactsPath  dotnet --artifacts-path for the build (default: <OutputDir>\build).
.PARAMETER SourceUrl      URL of the source repository, written into NOTICE.txt.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDir,
    [string]$ArtifactsPath,
    [string]$SourceUrl
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $repo 'dist' }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $OutputDir 'build' }
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
$templates = Join-Path $PSScriptRoot 'package'

if (-not $Version) {
    $Version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'src\LabApi\LabApi.csproj') -Raw)).Project.PropertyGroup.Version |
        Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw 'No <Version> in src\LabApi\LabApi.csproj; pass -Version.' }
}

$managed = Join-Path $repo '.runtime\server-original\Carl Mod_Data\Managed'
if (-not (Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) {
    throw "Reference server not found at $managed. Run 'python tools/extract-server.py' first."
}

# Commit information for NOTICE.txt.
$commit = (& git -C $repo rev-parse --short HEAD 2>$null)
if (-not $commit) { $commit = 'unknown' }
$dirty = (& git -C $repo status --porcelain --untracked-files=no 2>$null)
if ($dirty) {
    Write-Warning 'The working tree has uncommitted changes; the package does not match a commit.'
    $commit = "$commit (with uncommitted changes)"
}
if (-not $SourceUrl) {
    $SourceUrl = (& git -C $repo remote get-url origin 2>$null)
    if (-not $SourceUrl) { $SourceUrl = 'the LabAPIMobile source repository this package was built from' }
}

# Build.
Write-Host "Building LabApiMobile.sln (Release) into $ArtifactsPath"
& dotnet build (Join-Path $repo 'LabApiMobile.sln') -c Release --artifacts-path $ArtifactsPath -nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
$bin = Join-Path $ArtifactsPath 'bin'

# Stage.
$name = "LabApiMobile-$Version"
$stage = Join-Path $OutputDir $name
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage, (Join-Path $stage 'framework'), (Join-Path $stage 'plugins'), (Join-Path $stage 'licenses') | Out-Null

function Copy-Required([string]$Source, [string]$TargetDir) {
    if (-not (Test-Path -LiteralPath $Source)) { throw "Build output missing: $Source" }
    Copy-Item -LiteralPath $Source -Destination $TargetDir
}
function Copy-Optional([string]$Source, [string]$TargetDir) {
    if (Test-Path -LiteralPath $Source) { Copy-Item -LiteralPath $Source -Destination $TargetDir }
}

$installerBin = Join-Path $bin 'Installer\release'
foreach ($f in 'LabApiMobile.Installer.exe', 'LabApiMobile.Installer.exe.config', 'Mono.Cecil.dll') {
    Copy-Required (Join-Path $installerBin $f) $stage
}
$labApiBin = Join-Path $bin 'LabApi\release'
Copy-Required (Join-Path $labApiBin 'LabApi.dll') (Join-Path $stage 'framework')
Copy-Required (Join-Path $labApiBin '0Harmony.dll') (Join-Path $stage 'framework')
Copy-Optional (Join-Path $labApiBin 'LabApi.pdb') (Join-Path $stage 'framework')
Copy-Optional (Join-Path $labApiBin '0Harmony.pdb') (Join-Path $stage 'framework')
$merBin = Join-Path $bin 'ProjectMER\release'
Copy-Required (Join-Path $merBin 'ProjectMER.dll') (Join-Path $stage 'plugins')
Copy-Optional (Join-Path $merBin 'ProjectMER.pdb') (Join-Path $stage 'plugins')

# ProjectMER must not need anything the game, LabApi or Harmony do not provide.
$provided = @{}
Get-ChildItem -LiteralPath $managed -Filter *.dll | ForEach-Object { $provided[$_.BaseName] = $true }
foreach ($n in 'LabApi', '0Harmony') { $provided[$n] = $true }
$merRefs = [Reflection.AssemblyName[]]([Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $merBin 'ProjectMER.dll'))).GetReferencedAssemblies())
$missing = @($merRefs | Where-Object { -not $provided.ContainsKey($_.Name) } | ForEach-Object Name)
if ($missing.Count -gt 0) { throw "ProjectMER.dll references assemblies the package does not provide: $($missing -join ', ')" }

# Text files with CRLF line endings and placeholders filled in.
$gameVersions = '0.0.4'
function Write-Text([string]$Source, [string]$Target) {
    $text = [IO.File]::ReadAllText($Source)
    $text = $text.Replace('{{VERSION}}', $Version).Replace('{{COMMIT}}', $commit).Replace('{{SOURCE}}', $SourceUrl).Replace('{{GAME_VERSIONS}}', $gameVersions)
    $text = ($text -replace "`r`n", "`n") -replace "`n", "`r`n"
    [IO.File]::WriteAllText($Target, $text, (New-Object Text.UTF8Encoding($false)))
}
Write-Text (Join-Path $templates 'INSTALL.txt') (Join-Path $stage 'INSTALL.txt')
Write-Text (Join-Path $templates 'NOTICE.txt') (Join-Path $stage 'NOTICE.txt')
Get-ChildItem -LiteralPath (Join-Path $templates 'licenses') -Filter *.txt | ForEach-Object {
    Write-Text $_.FullName (Join-Path $stage "licenses\$($_.Name)")
}

# Zip with forward-slash entry names under one top folder.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zipPath = Join-Path $OutputDir "$name.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
        $entry = "$name/" + $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $zip.Dispose() }

Write-Host ''
Write-Host "Package: $zipPath ($([math]::Round((Get-Item -LiteralPath $zipPath).Length / 1KB)) KB)"
Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0,10:N0}  {1}' -f $_.Length, $_.FullName.Substring($stage.Length + 1)
}
