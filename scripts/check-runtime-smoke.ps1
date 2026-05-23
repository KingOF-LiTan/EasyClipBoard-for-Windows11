param(
    [string]$ExePath = "clip\clip\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\clip.exe",
    [int]$StartupTimeoutSeconds = 8,
    [int]$CaptureTimeoutSeconds = 12
)

$ErrorActionPreference = "Stop"

function Stop-ClipProcesses {
    Get-Process clip -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

function Get-ClipProcesses {
    @(Get-Process clip -ErrorAction SilentlyContinue | Sort-Object Id)
}

function Wait-ForFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $Path) {
            return $true
        }
        Start-Sleep -Milliseconds 250
    }

    return $false
}

function Test-FileContainsText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $buffer = New-Object byte[] $stream.Length
            [void]$stream.Read($buffer, 0, $buffer.Length)
        }
        finally {
            $stream.Dispose()
        }

        $content = [System.Text.Encoding]::UTF8.GetString($buffer)
        return $content.Contains($Text)
    }
    catch {
        return $false
    }
}

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$resolvedExe = Resolve-Path -LiteralPath (Join-Path $repoRoot $ExePath)
$dbPath = Join-Path $env:LOCALAPPDATA "WinClipboard\data.db"
$walPath = "$dbPath-wal"
$marker = "SMOKE_MARKER_{0:yyyyMMdd_HHmmss_fff}" -f (Get-Date)

Push-Location $repoRoot
try {
    Stop-ClipProcesses

    if (Test-Path -LiteralPath $dbPath) {
        Remove-Item -LiteralPath $dbPath -Force
    }
    if (Test-Path -LiteralPath $walPath) {
        Remove-Item -LiteralPath $walPath -Force
    }

    Start-Process -FilePath $resolvedExe
    Start-Sleep -Seconds 2

    $processes = Get-ClipProcesses
    if ($processes.Count -ne 1) {
        throw "Expected exactly one clip.exe after first launch, found $($processes.Count)."
    }

    $firstPath = $processes[0].Path
    if ($firstPath -ne $resolvedExe.Path) {
        throw "Expected clip.exe path '$($resolvedExe.Path)', found '$firstPath'."
    }

    if (-not (Wait-ForFile -Path $dbPath -TimeoutSeconds $StartupTimeoutSeconds)) {
        throw "Storage database was not created at '$dbPath' within $StartupTimeoutSeconds seconds."
    }

    Start-Process -FilePath $resolvedExe
    Start-Sleep -Seconds 2

    $processes = Get-ClipProcesses
    if ($processes.Count -ne 1) {
        $details = ($processes | ForEach-Object { "$($_.Id):$($_.Path)" }) -join ", "
        throw "Single-instance guard failed. Expected one clip.exe after second launch, found $($processes.Count): $details"
    }

    Set-Clipboard -Value $marker

    $deadline = (Get-Date).AddSeconds($CaptureTimeoutSeconds)
    $foundMarker = $false
    while ((Get-Date) -lt $deadline) {
        if ((Test-FileContainsText -Path $dbPath -Text $marker) -or
            (Test-FileContainsText -Path $walPath -Text $marker)) {
            $foundMarker = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }

    if (-not $foundMarker) {
        throw "Clipboard marker '$marker' was not found in data.db or WAL within $CaptureTimeoutSeconds seconds."
    }

    Write-Host "Runtime smoke passed. Process isolation, single-instance guard, storage init, and clipboard capture verified."
}
finally {
    Stop-ClipProcesses
    Pop-Location
}
