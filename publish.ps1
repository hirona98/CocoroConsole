Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# 引数なしで実行できる配布用 publish。
# 実体は advanced スクリプトに委譲する。
# $PSScriptRoot is reliable in non-interactive execution; fall back if missing.
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$advanced = Join-Path $scriptDir "publish-singlefile-win-x64.advanced.ps1"

if (-not (Test-Path $advanced)) {
    throw "Publish script not found: $advanced"
}

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $advanced -Clean
exit $LASTEXITCODE
