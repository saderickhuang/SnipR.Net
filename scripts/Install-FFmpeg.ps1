# Run this script from the extracted portable release before starting SnipR.
$ErrorActionPreference = 'Stop'
$toolsDir = Join-Path $PSScriptRoot 'tools'
$cacheDir = Join-Path $PSScriptRoot '.downloads'
New-Item -ItemType Directory -Force $toolsDir, $cacheDir | Out-Null
$url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
$archive = Join-Path $cacheDir 'ffmpeg-release-essentials.zip'
$checksum = Join-Path $cacheDir 'ffmpeg-release-essentials.zip.sha256'
Write-Host 'Downloading FFmpeg separately from Gyan (GPL build). See https://ffmpeg.org/legal.html'
Invoke-WebRequest "$url.sha256" -OutFile $checksum
$expected = ((Get-Content $checksum -Raw).Trim() -split '\s+')[0]
if ($expected -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid publisher checksum.' }
if (-not (Test-Path $archive) -or (Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) {
    & curl.exe -fL --retry 3 $url -o $archive
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg download failed.' }
}
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) { throw 'FFmpeg checksum mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($mapping in @(@('ffmpeg.exe','ffmpeg.exe'), @('LICENSE','FFmpeg-LICENSE.txt'), @('README.txt','FFmpeg-README.txt'))) {
        $entry = $zip.Entries | Where-Object Name -eq $mapping[0] | Select-Object -First 1
        if (-not $entry) { throw "Archive entry missing: $($mapping[0])" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $toolsDir $mapping[1]), $true)
    }
} finally { $zip.Dispose() }
Copy-Item $checksum (Join-Path $toolsDir 'FFmpeg-archive.sha256')
Write-Host 'Ready. You can now open SnipR.exe.'
