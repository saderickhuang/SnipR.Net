using System.Drawing;

namespace SnipR;

// Keep the selected rectangle through the first click of a double-click.
// Only actual movement past the drag threshold starts a replacement selection.
internal sealed class SelectionGesture(Rectangle desktop)
{
    public Rectangle Selection { get; private set; }
    public bool Pressed { get; private set; }
    public bool Dragging { get; private set; }
    private Point origin;
    private bool confirmationClick;

    public void Preview(Rectangle bounds)
    {
        if (Selection != bounds) confirmationClick = false;
        Selection = bounds;
    }
    public bool Down(Point point, int clickCount)
    {
        if (clickCount >= 2 && confirmationClick && Selection.Contains(point)) return true;
        origin = point;
        Pressed = true;
        Dragging = false;
        confirmationClick = false;
        return false;
    }
    public void Move(Point point, double thresholdX, double thresholdY)
    {
        if (!Pressed) return;
        if (!Dragging && Math.Abs(point.X - origin.X) < thresholdX && Math.Abs(point.Y - origin.Y) < thresholdY) return;
        Dragging = true;
        Selection = CaptureRegion.Normalize(origin, point, desktop);
    }
    public void Up(Point point, double thresholdX, double thresholdY)
    {
        if (!Pressed) return;
        Move(point, thresholdX, thresholdY);
        confirmationClick = !Dragging && Selection.Contains(point);
        Pressed = false;
        Dragging = false;
    }
}
