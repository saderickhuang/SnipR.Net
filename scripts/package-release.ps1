$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'src/SnipR/SnipR.csproj'
[xml]$projectXml = Get-Content $project
$version = [string]$projectXml.Project.PropertyGroup.Version
$releaseRoot = Join-Path $projectRoot 'artifacts/releases'
$stage = Join-Path $releaseRoot ('staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage | Out-Null
# Use a fresh output folder and never redistribute the downloaded FFmpeg build.
dotnet publish $project -c Release -r win-x64 --self-contained true -p:IncludeFfmpeg=false -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
if (Test-Path (Join-Path $stage 'tools/ffmpeg.exe')) { throw 'FFmpeg must not be in the public release archive.' }
Copy-Item (Join-Path $projectRoot 'LICENSE'), (Join-Path $projectRoot 'README.md'), (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md'), (Join-Path $projectRoot 'CONTRIBUTING.md'), (Join-Path $projectRoot 'CHANGELOG.md') $stage
$licenseDir = Join-Path $stage 'licenses'
New-Item -ItemType Directory -Force $licenseDir | Out-Null
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$assets = Get-Content (Join-Path $projectRoot 'src/SnipR/obj/project.assets.json') -Raw | ConvertFrom-Json
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') { continue }
    $package = Join-Path $nugetRoot $library.Name.ToLowerInvariant()
    foreach ($notice in Get-ChildItem $package -File | Where-Object { $_.Name -match '^(license|third.party.notices)(\..*)?$' }) {
        Copy-Item $notice.FullName (Join-Path $licenseDir ($library.Name.Replace('/','-') + '-' + $notice.Name))
    }
}
$runtime = Get-Content (Join-Path $stage 'SnipR.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $package = Join-Path $nugetRoot ($framework.name.ToLowerInvariant() + '.runtime.win-x64/' + $framework.version)
    $notices = @(Get-ChildItem $package -File | Where-Object { $_.Name -match '^(license|third.party.notices)(\..*)?$' })
    if ($notices.Count -eq 0) { throw "Missing runtime license for $($framework.name)." }
    foreach ($notice in $notices) { Copy-Item $notice.FullName (Join-Path $licenseDir ($framework.name + '-' + $notice.Name)) }
}
$zipPath = Join-Path $releaseRoot "SnipR-$version-win-x64.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -Force
$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zipPath.sha256", "$hash  $([IO.Path]::GetFileName($zipPath))`n")
Write-Host "Release archive: $zipPath"
Write-Host "Staging folder: $stage"
