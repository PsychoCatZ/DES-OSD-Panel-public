using System.Drawing;

namespace OsdPanel;

public static class PanelPlacement
{
    public static Point Clamp(Point position, Size size, Rectangle workArea) => new(
        Math.Max(workArea.Left, Math.Min(position.X, workArea.Right - size.Width)),
        Math.Max(workArea.Top, Math.Min(position.Y, workArea.Bottom - size.Height)));

    public static Point NearestCorner(Point position, Size size, Rectangle workArea)
    {
        var right = Math.Max(workArea.Left, workArea.Right - size.Width);
        var bottom = Math.Max(workArea.Top, workArea.Bottom - size.Height);
        return new Point(
            Math.Abs((long)position.X - workArea.Left) < Math.Abs((long)right - position.X) ? workArea.Left : right,
            Math.Abs((long)position.Y - workArea.Top) < Math.Abs((long)bottom - position.Y) ? workArea.Top : bottom);
    }
}
