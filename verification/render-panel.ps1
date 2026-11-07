$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$project = Join-Path $PSScriptRoot 'Checks\Checks.csproj'
& $dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Checks build failed.' }
& $dotnet (Join-Path $PSScriptRoot 'Checks\bin\Release\net8.0-windows\Checks.dll')
if ($LASTEXITCODE -ne 0) { throw 'Rendering and verification failed.' }