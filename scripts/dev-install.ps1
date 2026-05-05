<#
.SYNOPSIS
  Build the mod and link the dev staging directory into the VS Mods folder.

.DESCRIPTION
  Builds src/TemporalSiege.csproj which stages the mod at dist/dev/temporalsiege/.
  Then creates a junction at %APPDATA%\VintagestoryData\Mods\temporalsiege pointing
  at the staging dir, so subsequent rebuilds are picked up by VS automatically.

  Junctions don't require admin elevation on Windows.

.PARAMETER Configuration
  Build configuration. Default: Debug.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repoRoot     = Resolve-Path (Join-Path $PSScriptRoot '..')
$csproj       = Join-Path $repoRoot 'src\TemporalSiege.csproj'
$stagingDir   = Join-Path $repoRoot 'dist\dev\temporalsiege'
$modsDir      = Join-Path $env:APPDATA 'VintagestoryData\Mods'
$modLink      = Join-Path $modsDir 'temporalsiege'

Write-Host "[dev-install] Building $csproj ($Configuration)..."
& dotnet build $csproj -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }

if (-not (Test-Path $stagingDir)) {
    throw "Staging dir not found after build: $stagingDir"
}

if (-not (Test-Path $modsDir)) {
    Write-Host "[dev-install] Creating $modsDir"
    New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
}

if (Test-Path $modLink) {
    $item = Get-Item $modLink -Force
    if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        Write-Host "[dev-install] Removing existing junction at $modLink"
        cmd /c "rmdir `"$modLink`""
    } else {
        throw "$modLink exists and is not a junction. Delete it manually before re-running."
    }
}

Write-Host "[dev-install] Linking $modLink -> $stagingDir"
cmd /c "mklink /J `"$modLink`" `"$stagingDir`"" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "mklink failed (exit $LASTEXITCODE)" }

Write-Host "[dev-install] Done. Launch Vintage Story; mod should appear as 'Temporal Siege'."
