$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$wwwroot = Join-Path $repoRoot "clip\clip\wwwroot"
$stylesPath = Join-Path $wwwroot "styles.css"
$indexPath = Join-Path $wwwroot "index.html"
$listActionsPath = Join-Path $wwwroot "js\listActions.js"
$listViewPath = Join-Path $wwwroot "js\listView.js"
$vaultPath = Join-Path $wwwroot "js\vault.js"

$styles = Get-Content -Raw -LiteralPath $stylesPath
$index = Get-Content -Raw -LiteralPath $indexPath
$listActions = Get-Content -Raw -LiteralPath $listActionsPath
$listView = Get-Content -Raw -LiteralPath $listViewPath
$vault = Get-Content -Raw -LiteralPath $vaultPath

# ── Inline event handler check (HTML + generated HTML in JS) ──
$inlineEvents = @('onclick=', 'onchange=', 'oninput=', '.onclick', '.onchange', '.oninput')
foreach ($event in $inlineEvents) {
    if ($index -match [regex]::Escape($event)) {
        throw "HTML regression: $event inline handler found in index.html."
    }
}

# Also scan JS files for generated HTML strings with inline handlers
Get-ChildItem -LiteralPath (Join-Path $wwwroot "js") -Filter "*.js" | ForEach-Object {
    $js = Get-Content -Raw -LiteralPath $_.FullName
    foreach ($event in $inlineEvents) {
        if ($js -match [regex]::Escape($event)) {
            throw "HTML regression: $event inline handler found in generated HTML within $($_.Name)."
        }
    }
}

# ── Click semantics ──
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

# ── Selected card action rail ──
if ($styles -notmatch "\.card\.selected\s+\.card-actions") {
    throw "Interaction affordance missing: selected cards must reveal card actions without hover."
}

# ── Preview affordance ──
if ($listView -notmatch "action\.preview") {
    throw "Interaction affordance missing: preview action must exist in card actions."
}
if ($listView -notmatch "case 'preview':") {
    throw "Interaction handler missing: preview case in bindListEvents."
}

# ── Destructive action confirm ──
if (($listActions -match "await app\.bridge\.send\('delete'") -and
    ($listActions -notmatch "showConfirm" -or $listActions -notmatch "confirm\.deleteItem")) {
    throw "Interaction regression: delete must call showConfirm before bridge delete."
}
if (($vault -match "app\.bridge\.send\('deleteSecret'") -and
    ($vault -notmatch "showConfirm")) {
    throw "Interaction regression: deleteSecret must call showConfirm before bridge deleteSecret."
}

# ── Toast module existence ──
$toastPath = Join-Path $wwwroot "js\toast.js"
if (-not (Test-Path $toastPath)) {
    throw "UX affordance missing: toast.js not found."
}

# ── Double-click paste path (manual click-counter detection) ──
if ($listView -notmatch "DBLCLICK_WINDOW") {
    throw "Interaction regression: manual double-click detection via click counter missing in listView.js."
}
$onDoubleClickMatch = [regex]::Match($listActions, "function onCardDoubleClick\([^)]*\)\s*\{(?<body>[\s\S]*?)\n\s*\}")
if ($onDoubleClickMatch.Success -and $onDoubleClickMatch.Groups["body"].Value -notmatch "paste\s*\(") {
    throw "Interaction regression: onCardDoubleClick must call paste."
}

# ── Immediate paste close (no delay) ──
if ($listActions -match "setTimeout.*hideWindow") {
    throw "Interaction regression: paste must NOT delay hideWindow."
}
if ($vault -match "setTimeout.*hideWindow") {
    throw "Interaction regression: vault copy must NOT delay hideWindow."
}

# ── Help open (keyboard + button) ──
$keyboardPath = Join-Path $wwwroot "js\keyboard.js"
$keyboard = Get-Content -Raw -LiteralPath $keyboardPath
if ($keyboard -notmatch "e\.key === '/' \&\& e\.shiftKey" -or $keyboard -notmatch "\?") {
    throw "Help key missing: keyboard.js must handle both '?' and Shift+/."
}
if ($index -notmatch "btn-help") {
    throw "Help affordance missing: help icon button must exist in index.html."
}

# ── Drag shield documentation ──
$dragPath = Join-Path $wwwroot "js\drag.js"
$drag = Get-Content -Raw -LiteralPath $dragPath
if ($drag -notmatch "Shield duration") {
    throw "Drag documentation missing: anti-click shield duration should be documented in drag.js."
}

Write-Host "Interaction affordance check passed."
