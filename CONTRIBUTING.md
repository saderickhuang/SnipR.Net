# Contributing

欢迎提交问题报告或 Pull Request。项目使用 C#、.NET 10 和 WPF，需要 Windows 环境。

1. Fork 并克隆仓库。
2. 按 README 下载 FFmpeg，执行 `dotnet build SnipR.slnx -c Release`。
3. 修改后执行 `scripts/verify.ps1 -WithBorder`；声音改动额外使用 `-WithAudio`。
4. 提交 PR，说明修改目的、验证结果和已知限制。

问题报告请包含 Windows 版本、显示缩放、显示器数量、复现步骤和错误信息。请勿上传包含私人内容的录屏、音频、账户信息或本机凭据。

提交的贡献按本项目 MIT 许可证发布。第三方依赖继续遵守各自的许可证。发布打包使用 `scripts/package-release.ps1`，不要将本机下载的 FFmpeg 二进制提交到仓库或直接加入发布附件。
