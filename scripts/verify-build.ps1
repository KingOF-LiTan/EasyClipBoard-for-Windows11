param(
    [string]$ProjectPath = "clip\clip\clip.csproj"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    dotnet build $ProjectPath `
        /p:WindowsPackageType=None `
        /p:EnableMsixTooling=false `
        /p:DisableMsixProjectCapabilityAddedByProject=true `
        /p:GenerateAppxPackageOnBuild=false `
        /p:AppxPackage=false
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
