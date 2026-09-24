#!/usr/bin/env pwsh

<#
.SYNOPSIS
Builds the solution and runs both test suites.

.DESCRIPTION
Two suites and two runners: the three older ones go through dotnet test, and
the interface suite runs itself, xunit v3 having no VSTest adapter. Until they
are all on v3, checking the tree means three commands, which this is.

Built once and run with --no-build, so that what the suites run is what was
just compiled and not a second compilation of it.

.PARAMETER Configuration
Debug by default; Release is what CI builds.
#>

param(
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

function Step([string] $Name, [scriptblock] $Command) {
    Write-Host ""
    Write-Host "== $Name" -ForegroundColor Cyan

    & $Command

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "$Name failed." -ForegroundColor Red

        exit $LASTEXITCODE
    }
}

Push-Location $PSScriptRoot

# A run reads better on a clean screen than under the one before it.
Clear-Host

try {
    Step 'Build' { dotnet build --configuration $Configuration }
    Step 'Tests' { dotnet test --no-build --configuration $Configuration }
    Step 'Interface tests' { dotnet run --no-build --configuration $Configuration --project tests/Koma.Desktop.Tests }

    Write-Host ""
    Write-Host "All green." -ForegroundColor Green
}
finally {
    Pop-Location
}
