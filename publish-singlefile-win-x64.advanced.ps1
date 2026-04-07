<#
.SYNOPSIS
    Publish a single-file release build for CocoroConsole.
.DESCRIPTION
    Creates a self-contained single-file output into the publish folder.
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "publish"
)

# Strict mode and error handling.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolve paths.
$repoRoot = Resolve-Path $PSScriptRoot
$projectPath = Join-Path $repoRoot "CocoroConsole.csproj"
$outDir = Join-Path $repoRoot $Output

# Validate inputs.
if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

# Clean output folders when requested.
if ($Clean -and (Test-Path $outDir)) {
    Remove-Item -LiteralPath $outDir -Recurse -Force
}

# Clean legacy output to avoid confusion.
$legacyOutDir = Join-Path $repoRoot "artifacts\\publish\\singlefile-win-x64"
if ($Clean -and (Test-Path $legacyOutDir)) {
    Remove-Item -LiteralPath $legacyOutDir -Recurse -Force
}

# Ensure output directory exists.
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# Publish settings display.
Write-Host "Publishing single-file build..." -ForegroundColor Cyan
Write-Host "  Project : $projectPath"
Write-Host "  Config  : $Configuration"
Write-Host "  Runtime : $Runtime"
Write-Host "  Output  : $outDir"

# Publish with explicit argument array to avoid line-continuation issues.
$publishArgs = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $outDir,
    "--nologo",
    "-p:SelfContained=true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:PublishTrimmed=false",
    "-p:PublishReadyToRun=true"
)
& dotnet @publishArgs

# Remove unnecessary artifacts for distribution.
Get-ChildItem -LiteralPath $outDir -File -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $outDir -File -Filter "*.lib" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

# Done message.
Write-Host "Done." -ForegroundColor Green
Write-Host "Publish output: $outDir"
