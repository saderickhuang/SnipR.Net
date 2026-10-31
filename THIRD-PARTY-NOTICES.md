# Third-party components

SnipR's own source code is licensed under the MIT license in `LICENSE`. That license does not replace the licenses of third-party components.

## .NET / WPF

Microsoft .NET runtime and Windows Desktop runtime. Source and notices:

- https://github.com/dotnet/runtime
- https://github.com/dotnet/wpf

## NAudio 2.2.1

Copyright Mark Heath and contributors. MIT license.

- https://github.com/naudio/NAudio/tree/v2.2.1
- https://github.com/naudio/NAudio/blob/v2.2.1/license.txt

NAudio is used for WASAPI capture and silent loopback clock playback. The application uses the 2.x API explicitly; it does not require the newer 3.x API.

## FFmpeg

FFmpeg is a separate, unmodified executable, invoked for gdigrab desktop capture, H.264 encoding, AAC mixing and MP4 muxing.

- Project: https://ffmpeg.org/
- Windows binaries: https://www.gyan.dev/ffmpeg/builds/
- Documentation: https://ffmpeg.org/ffmpeg-devices.html#gdigrab
- Source: https://github.com/FFmpeg/FFmpeg

The public GitHub release does **not** redistribute FFmpeg. On the first recording attempt, SnipR downloads it directly from Gyan over HTTPS, verifies the publisher's SHA256 and installs it in `%LOCALAPPDATA%/SnipR/components/ffmpeg`. Users may instead provide their own executable in the application's `tools` directory. The Gyan essentials build includes GPL components such as libx264. The automatic installer retains its license, README and archive checksum alongside the executable. It follows the publisher's release URL, so the exact FFmpeg version can change on a fresh download; an existing installation is reused without automatic updates. Run the installed `ffmpeg.exe -version` to identify that build. FFmpeg licensing information: https://ffmpeg.org/legal.html

The portable release includes .NET runtime license / third-party notices and NAudio license texts in the `licenses` folder. `scripts/publish.ps1` creates a local-use bundle with FFmpeg; `scripts/package-release.ps1` creates the public archive without FFmpeg.
