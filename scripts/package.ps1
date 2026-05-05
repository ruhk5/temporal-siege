<#
.SYNOPSIS
  Build the mod in Release and produce a distributable zip at dist/voxelengine-<version>.zip.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot '..')
$csproj     = Join-Path $repoRoot 'src\VoxelEngine.csproj'
$stagingDir = Join-Path $repoRoot 'dist\dev\voxelengine'
$distDir    = Join-Path $repoRoot 'dist'

# Pull the version out of modinfo.json so the zip name tracks releases.
$modinfo = Get-Content (Join-Path $repoRoot 'modinfo.json') -Raw | ConvertFrom-Json
$version = $modinfo.version
$zipPath = Join-Path $distDir ("voxelengine-{0}.zip" -f $version)

Write-Host "[package] Building $csproj (Release)..."
& dotnet build $csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Host "[package] Zipping $stagingDir -> $zipPath"
Compress-Archive -Path (Join-Path $stagingDir '*') -DestinationPath $zipPath -Force

Write-Host "[package] Done: $zipPath"
