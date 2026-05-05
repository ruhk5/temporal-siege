<#
.SYNOPSIS
  Build the mod in Release and produce a distributable zip at dist/temporalsiege-<version>.zip.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot '..')
$csproj     = Join-Path $repoRoot 'src\TemporalSiege.csproj'
$stagingDir = Join-Path $repoRoot 'dist\dev\temporalsiege'
$distDir    = Join-Path $repoRoot 'dist'

# Pull modid + version out of modinfo.json so the zip name tracks releases.
$modinfo = Get-Content (Join-Path $repoRoot 'modinfo.json') -Raw | ConvertFrom-Json
$modid   = $modinfo.modid
$version = $modinfo.version
$zipPath = Join-Path $distDir ("{0}-{1}.zip" -f $modid, $version)

Write-Host "[package] Building $csproj (Release)..."
& dotnet build $csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Host "[package] Zipping $stagingDir -> $zipPath"
Compress-Archive -Path (Join-Path $stagingDir '*') -DestinationPath $zipPath -Force

Write-Host "[package] Done: $zipPath"
