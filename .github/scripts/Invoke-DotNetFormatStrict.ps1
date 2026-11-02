param(
    [string[]]$Targets,
    [switch]$DiscoverFromCurrentDirectory,
    [switch]$RestoreTargets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Targets -or $Targets.Count -eq 0) {
    if (-not $DiscoverFromCurrentDirectory) {
        throw 'Specify -Targets or pass -DiscoverFromCurrentDirectory.'
    }

    $solutionFile = Get-ChildItem -Path . -File | Where-Object { $_.Extension -eq '.sln' -or $_.Extension -eq '.slnx' } | Select-Object -First 1
    if ($solutionFile) {
        $Targets = @($solutionFile.FullName)
    } else {
        $Targets = @(Get-ChildItem -Path . -Recurse -Filter *.csproj | Select-Object -ExpandProperty FullName)
    }
}

if (-not $Targets -or $Targets.Count -eq 0) {
    throw 'No solution or project files found for format validation.'
}

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE."
    }
}

foreach ($target in $Targets) {
    if ($RestoreTargets) {
        Invoke-Dotnet restore $target
    }

    Invoke-Dotnet format whitespace $target --verify-no-changes --no-restore
    Invoke-Dotnet format style $target --verify-no-changes --no-restore --severity warn
    Invoke-Dotnet format analyzers $target --verify-no-changes --no-restore --severity warn
}
