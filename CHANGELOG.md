# Changelog

## 0.1.0 — 2026-10-31

First public preview release.

- WPF desktop interface with collapsible settings and persisted preferences.
- Freehand region selection and automatic window-content selection.
- Double-click or Enter to confirm; three-second countdown.
- Visible, click-through recording boundary and floating stop control.
- System audio and microphone recording, independently selectable.
- H.264 / AAC MP4 output, 15 / 30 / 60 FPS options and optional cursor capture.
- Global start/stop shortcuts and retained scratch files on save failure.

Windows x64 only. Window selection records a fixed screen region; it does not follow moved windows or record obscured windows. Pause/resume, editing, hardware encoding, long-duration stress testing and mixed-DPI multi-monitor verification are not included in this preview.

The downloadable archive includes the .NET runtime. FFmpeg is a separate dependency installed with the bundled `Install-FFmpeg.ps1` script.
