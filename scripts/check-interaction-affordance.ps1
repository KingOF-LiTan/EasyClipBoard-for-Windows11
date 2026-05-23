$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$stylesPath = Join-Path $repoRoot "clip\clip\wwwroot\styles.css"
$listActionsPath = Join-Path $repoRoot "clip\clip\wwwroot\js\listActions.js"

$styles = Get-Content -Raw -LiteralPath $stylesPath
$listActions = Get-Content -Raw -LiteralPath $listActionsPath

if ($listActions -notmatch "single click = select only") {
    throw "Interaction contract missing: listActions.js should document single-click selection semantics."
}

$onCardClickMatch = [regex]::Match($listActions, "function onCardClick\([^)]*\)\s*\{(?<body>[\s\S]*?)\n\s*\}")
if (-not $onCardClickMatch.Success) {
    throw "Interaction contract missing: could not find onCardClick function."
}

if ($onCardClickMatch.Groups["body"].Value -match "paste\s*\(") {
    throw "Interaction regression: onCardClick must not paste directly."
}

if ($styles -notmatch "\.card\.selected\s+\.card-actions") {
    throw "Interaction affordance missing: selected cards must reveal card actions without requiring hover."
}

Write-Host "Interaction affordance check passed."
