#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

Set-Location -LiteralPath $PSScriptRoot

$Configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { 'Release' }
$Project = 'src/Velvet.SourceGenerators/Velvet.SourceGenerators.csproj'
$OutputDll = "src/Velvet.SourceGenerators/bin/$Configuration/netstandard2.0/Velvet.SourceGenerators.dll"
$DeployDir = '../Runtime/Plugins/Generators'

Write-Host "[Velvet.SourceGenerators] dotnet build -c $Configuration"
dotnet build $Project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

if (-not (Test-Path -LiteralPath $DeployDir)) {
    New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null
}
Copy-Item -LiteralPath $OutputDll -Destination (Join-Path $DeployDir 'Velvet.SourceGenerators.dll') -Force
Write-Host "[Velvet.SourceGenerators] Deployed to $DeployDir/Velvet.SourceGenerators.dll"
