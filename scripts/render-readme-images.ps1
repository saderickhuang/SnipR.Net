# Render the real WPF controls without opening desktop windows or recording the screen.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$work = Join-Path $projectRoot 'artifacts/readme-renderer'
$output = Join-Path $projectRoot 'docs/assets'
New-Item -ItemType Directory -Force $work, $output | Out-Null
$projectPath = [System.Security.SecurityElement]::Escape((Join-Path $projectRoot 'src/SnipR/SnipR.csproj'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="$projectPath" /></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $work 'ReadmeRenderer.csproj'), $project)
$source = @'
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipR;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        var main = new MainWindow();
        ((TextBlock)main.FindName("FolderText")).Text = @"D:\Videos\SnipR";
        Render(main, args[0], "main-window.png");
        main = new MainWindow();
        ((TextBlock)main.FindName("FolderText")).Text = @"D:\Videos\SnipR";
        ((Expander)main.FindName("SettingsExpander")).IsExpanded = true;
        Render(main, args[0], "recording-settings.png");
        var bar = new RecordingBar();
        bar.Update("00:00:12", "\u5f55\u5236\u4e2d  \u00b7  Ctrl + Shift + S \u505c\u6b62", "\u25a0 \u505c\u6b62");
        Render(bar, args[0], "recording-toolbar.png");
        // No Show / Run / Close: do not register hotkeys, capture audio, or save settings.
    }

    private static void Render(Window window, string directory, string name)
    {
        var content = (FrameworkElement)window.Content;
        if (content is Border border && border.Background == null) border.Background = window.Background;
        content.Measure(new Size(window.Width, double.PositiveInfinity));
        var height = double.IsNaN(window.Height) ? content.DesiredSize.Height : window.Height;
        content.Arrange(new Rect(0, 0, window.Width, height));
        content.UpdateLayout();
        const double scale = 2;
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.Width * scale),
            (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var path = Path.Combine(directory, name);
        using var file = File.Create(path);
        encoder.Save(file);
        Console.WriteLine($"Rendered {name}: {image.PixelWidth} x {image.PixelHeight}");
    }
}
'@
[IO.File]::WriteAllText((Join-Path $work 'Program.cs'), $source)
dotnet run --project (Join-Path $work 'ReadmeRenderer.csproj') -c Release -p:IncludeFfmpeg=false -- $output
if ($LASTEXITCODE -ne 0) { throw 'README image rendering failed.' }
