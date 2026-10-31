# SnipR 视觉素材

## Logo

文件：[`assets/snipr-logo.png`](assets/snipr-logo.png)。透明背景 PNG，适合深浅色 README 背景。

深色圆角底板沿用应用的深色界面；薄荷绿四角表示自由框选，中央珊瑚红圆点表示录制。标志不包含文字，便于缩小显示。当前用于项目文档；已发布 v0.1.1 的应用图标未改动。

Logo 使用内置 imagegen 生成，随项目采用 MIT 许可证。最终生成提示词：

> Create one finished professional app logo icon for SnipR, a minimalist Windows region screen recorder. Square 1024x1024 output with transparent background outside the icon. Single centered dark charcoal rounded-square tile (#101718), occupying 86 percent of canvas. Inside the tile draw four precise mint green (#B7F5CF) right-angle crop/selection corner brackets, thick uniform strokes with gently rounded ends, forming an implied square. In the exact center place one solid small coral recording circle (#FF766C), diameter about one fifth of the implied selection square. Ultra-clean flat vector-like geometry, optically balanced, crisp edges, generous negative space, recognizable at 24px. No letters, no typography, no text, no gradients, no shadows, no bevel, no 3D, no mockup, no extra symbols, no texture, no decorative objects. Just the single polished logo asset on genuine transparency. This is the final repository brand mark.

## 界面图

`assets/main-window.png`、`assets/recording-settings.png` 和 `assets/recording-toolbar.png` 由项目的实际 WPF 控件渲染，使用 2 倍分辨率。保存路径与计时为演示值，不包含桌面内容或本机用户名。

在 Windows 与 .NET 10 SDK 环境重新生成：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/render-readme-images.ps1
```

渲染脚本不打开桌面窗口、不录屏，也不保存个人设置。它会在 `artifacts/readme-renderer` 生成临时渲染项目，输出到 `docs/assets`。
