$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $projectRoot 'tools/ffmpeg/ffmpeg.exe'))) { & (Join-Path $PSScriptRoot 'setup-ffmpeg.ps1') }
dotnet publish (Join-Path $projectRoot 'src/SnipR/SnipR.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $projectRoot 'artifacts/SnipR-win-x64')
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item (Join-Path $projectRoot 'README.md'), (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md'), (Join-Path $projectRoot 'VALIDATION.md') (Join-Path $projectRoot 'artifacts/SnipR-win-x64')
