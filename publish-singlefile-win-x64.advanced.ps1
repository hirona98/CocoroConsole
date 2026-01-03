[CmdletBinding()]
param(
    [switch]$Clean,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "publish"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path $PSScriptRoot
$projectPath = Join-Path $repoRoot "CocoroConsole.csproj"
$outDir = Join-Path $repoRoot $Output

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

if ($Clean -and (Test-Path $outDir)) {
    Remove-Item -LiteralPath $outDir -Recurse -Force
}

# 旧出力先 (artifacts\publish\singlefile-win-x64) が残っていると紛らわしいため、クリーン時に掃除
$legacyOutDir = Join-Path $repoRoot "artifacts\\publish\\singlefile-win-x64"
if ($Clean -and (Test-Path $legacyOutDir)) {
    Remove-Item -LiteralPath $legacyOutDir -Recurse -Force
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "Publishing single-file build..." -ForegroundColor Cyan
Write-Host "  Project : $projectPath"
Write-Host "  Config  : $Configuration"
Write-Host "  Runtime : $Runtime"
Write-Host "  Output  : $outDir"

# WPFでは PublishTrimmed は基本的に安全でないため無効のまま。
# 単一ファイルでのネイティブ依存関係は自己展開。
& dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    -o $outDir `
    --nologo `
    -p:SelfContained=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=true

# 配布用: デバッグシンボルやリンク用の *.lib は同梱不要なので削除
Get-ChildItem -LiteralPath $outDir -File -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $outDir -File -Filter "*.lib" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "Done." -ForegroundColor Green
Write-Host "Publish output: $outDir"