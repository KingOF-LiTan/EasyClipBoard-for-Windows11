$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$wwwroot = Join-Path $repoRoot "clip\clip\wwwroot"
$files = @(
    (Join-Path $wwwroot "app.js")
) + (Get-ChildItem -LiteralPath (Join-Path $wwwroot "js") -Filter "*.js" | Sort-Object Name | ForEach-Object { $_.FullName })

foreach ($file in $files) {
    node --check $file
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Write-Host "Frontend JavaScript syntax check passed."
