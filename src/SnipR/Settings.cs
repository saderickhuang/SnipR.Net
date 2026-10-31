using System.Text.Json;

namespace SnipR;

internal sealed class Settings
{
    public bool SystemAudio { get; set; } = true;
    public bool Microphone { get; set; } = true;
    public bool ShowCursor { get; set; } = true;
    public int Fps { get; set; } = 30;
    public bool PreferWindowSelection { get; set; }
    public bool IncludeWindowFrame { get; set; }
    public string OutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "SnipR");
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipR", "settings.json");

    public static Settings Load(string? path = null)
    {
        try
        {
            var result = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path ?? FilePath)) ?? new();
            if (result.Fps is not (15 or 30 or 60)) result.Fps = 30;
            if (string.IsNullOrWhiteSpace(result.OutputFolder)) result.OutputFolder = new Settings().OutputFolder;
            return result;
        }
        catch { return new(); }
    }

    public void Save(string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
