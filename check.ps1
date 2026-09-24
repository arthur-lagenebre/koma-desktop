#!/usr/bin/env pwsh

<#
.SYNOPSIS
Builds the solution and runs the tests.

.DESCRIPTION
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

    Write-Host ""
    Write-Host "All green." -ForegroundColor Green
}
finally {
    Pop-Location
}
