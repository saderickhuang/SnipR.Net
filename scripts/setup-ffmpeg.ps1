$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$cacheDir = Join-Path $projectRoot '.downloads'
$toolsDir = Join-Path $projectRoot 'tools/ffmpeg'
New-Item -ItemType Directory -Force $cacheDir, $toolsDir | Out-Null
$archive = Join-Path $cacheDir 'ffmpeg-release-essentials.zip'
$url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
$checksumFile = "$archive.sha256"
Invoke-WebRequest "$url.sha256" -OutFile $checksumFile
$expected = ((Get-Content $checksumFile -Raw).Trim() -split '\s+')[0]
if (-not (Test-Path $archive) -or (Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) {
    & curl.exe -fL --retry 3 $url -o $archive
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg download failed.' }
}
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) { throw 'FFmpeg checksum mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($name in @('ffmpeg.exe','ffprobe.exe','LICENSE','README.txt')) {
        $entry = $zip.Entries | Where-Object { $_.Name -eq $name } | Select-Object -First 1
        if ($entry) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $toolsDir $name), $true) }
    }
} finally { $zip.Dispose() }
Copy-Item $checksumFile (Join-Path $toolsDir 'archive.sha256')
Write-Host "FFmpeg installed and SHA256 verified: $toolsDir"
