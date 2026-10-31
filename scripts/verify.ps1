param([switch]$WithAudio, [switch]$WithBorder, [switch]$AtScreenEdge)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'src/SnipR/SnipR.csproj'
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$exe = Join-Path $projectRoot 'src/SnipR/bin/Release/net10.0-windows/SnipR.exe'
$probe = Join-Path $projectRoot 'tools/ffmpeg/ffprobe.exe'
$encoder = Join-Path $projectRoot 'tools/ffmpeg/ffmpeg.exe'
$output = Join-Path $projectRoot ('artifacts/verification-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$arguments = '--self-test --output "' + $output + '"'
if ($WithAudio) { $arguments += ' --system-audio --microphone' }
if ($WithBorder) { $arguments += ' --border' }
if ($AtScreenEdge) { $arguments += ' --screen-edge' }
# This is an interactive test surface: it must be visible to verify recorded pixels.
$testProcess = Start-Process $exe -ArgumentList $arguments -WindowStyle Normal -PassThru -Wait
$report = Get-Content (Join-Path $output 'report.json') -Raw | ConvertFrom-Json
if ($testProcess.ExitCode -ne 0 -or $report.result -ne 'passed') { throw "Recording failed: $($report.error)" }
$metadata = (& $probe -v error -show_streams -show_format -of json $report.video) | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect video.' }
$video = $metadata.streams | Where-Object codec_type -eq 'video'
if ($video.codec_name -ne 'h264' -or $video.width -ne $report.width -or $video.height -ne $report.height -or $video.r_frame_rate -ne '30/1') { throw 'Unexpected video format or dimensions.' }
if ([double]::Parse($metadata.format.duration, [Globalization.CultureInfo]::InvariantCulture) -lt 3) { throw 'Video is too short.' }
if ($WithAudio) {
    $audio = $metadata.streams | Where-Object codec_type -eq 'audio'
    if ($audio.codec_name -ne 'aac' -or $audio.sample_rate -ne '48000') { throw 'Missing AAC audio.' }
    $volumeLog = Join-Path $output 'audio-volume.log'
    $volumeProcess = Start-Process $encoder -ArgumentList @('-hide_banner', '-i', ('"' + $report.video + '"'), '-af', 'volumedetect', '-vn', '-f', 'null', 'NUL') -WindowStyle Hidden -RedirectStandardError $volumeLog -PassThru -Wait
    $volumeText = Get-Content $volumeLog -Raw
    if ($volumeProcess.ExitCode -ne 0 -or $volumeText -notmatch 'max_volume:\s+(-?[\d.]+) dB') { throw 'Could not measure audio.' }
    $peak = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
    if ($peak -lt -70) { throw 'The audio track is unexpectedly silent.' }
}
& $encoder -v error -i $report.video -f null NUL
if ($LASTEXITCODE -ne 0) { throw 'Video decode failed.' }
& $encoder -v error -ss 2 -i $report.video -frames:v 1 -update 1 -y (Join-Path $output 'frame.png')
if ($LASTEXITCODE -ne 0) { throw 'Frame extraction failed.' }
$metadata | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'metadata.json') -Encoding UTF8
Write-Host "Passed: geometry, capture, encoding, dimensions, FPS, duration, decoding. Output: $output"
