using System.Drawing;

namespace SnipR;

internal static class BehaviorChecks
{
    internal static void Run(string output)
    {
        var desktop = new Rectangle(-1920, -200, 5760, 2360);
        var gesture = new SelectionGesture(desktop);
        Require(!gesture.Down(new(100, 100), 1), "First press must not confirm");
        gesture.Move(new(500, 400), 6, 6);
        gesture.Up(new(500, 400), 6, 6);
        var original = new Rectangle(100, 100, 400, 300);
        Require(gesture.Selection == original, "Initial drag");
        Require(!gesture.Down(new(200, 200), 1), "First confirmation click only arms confirmation");
        gesture.Up(new(202, 201), 6, 6);
        Require(gesture.Selection == original, "First click and small jitter preserve selection");
        Require(gesture.Down(new(202, 201), 2), "Second click confirms existing selection");
        var outside = new SelectionGesture(desktop);
        outside.Preview(original);
        outside.Down(new(800, 800), 1); outside.Up(new(800, 800), 6, 6);
        Require(!outside.Down(new(800, 800), 2), "Double-click outside selection must not confirm");
        outside.Move(new(1000, 1000), 6, 6); outside.Up(new(1000, 1000), 6, 6);
        Require(outside.Selection == new Rectangle(800, 800, 200, 200), "Drag replaces selection");
        var hovered = new SelectionGesture(desktop);
        hovered.Preview(original);
        hovered.Down(new(150, 150), 1); hovered.Up(new(150, 150), 6, 6);
        Require(hovered.Down(new(150, 150), 2), "Window preview confirms on double-click");

        var settingsFile = Path.Combine(output, "settings-roundtrip.json");
        new Settings { Fps = 60, SystemAudio = false, Microphone = true, ShowCursor = false,
            IncludeWindowFrame = true, PreferWindowSelection = true, OutputFolder = Path.Combine(output, "视频") }.Save(settingsFile);
        var loaded = Settings.Load(settingsFile);
        Require(loaded.Fps == 60 && !loaded.SystemAudio && loaded.Microphone && !loaded.ShowCursor
            && loaded.IncludeWindowFrame && loaded.PreferWindowSelection && loaded.OutputFolder == Path.Combine(output, "视频"), "Settings reload");
        loaded.Fps = 15; loaded.Save(settingsFile);
        Require(Settings.Load(settingsFile).Fps == 15, "Settings overwrite");
    }

    internal static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Behavior check failed: " + name);
    }
}
