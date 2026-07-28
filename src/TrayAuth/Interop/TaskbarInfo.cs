namespace TrayAuth.Interop;

public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

public readonly record struct TaskbarPlacement(TaskbarEdge Edge, Rectangle WorkArea, Rectangle ScreenBounds)
{
    public bool IsHorizontal => Edge is TaskbarEdge.Bottom or TaskbarEdge.Top;
}

/// <summary>
/// Works out which screen edge the taskbar is docked to, and how big the usable area is.
///
/// This compares <see cref="Screen.Bounds"/> against <see cref="Screen.WorkingArea"/> rather than
/// asking the shell directly: it needs no interop, and it is correct for every taskbar position and
/// for secondary monitors, which is all the panel needs to place itself.
/// </summary>
public static class TaskbarInfo
{
    public static TaskbarPlacement ForPoint(Point point)
    {
        Screen screen = Screen.FromPoint(point);
        return For(screen);
    }

    public static TaskbarPlacement ForPrimary() => For(Screen.PrimaryScreen ?? Screen.AllScreens[0]);

    public static TaskbarPlacement For(Screen screen)
    {
        Rectangle bounds = screen.Bounds;
        Rectangle work = screen.WorkingArea;

        // Whichever side lost the most room is where the taskbar is. An auto-hidden taskbar
        // reserves only a pixel or two, which still points at the right edge.
        int bottom = bounds.Bottom - work.Bottom;
        int top = work.Top - bounds.Top;
        int left = work.Left - bounds.Left;
        int right = bounds.Right - work.Right;

        TaskbarEdge edge = TaskbarEdge.Bottom;
        int largest = bottom;

        if (top > largest)
        {
            edge = TaskbarEdge.Top;
            largest = top;
        }

        if (left > largest)
        {
            edge = TaskbarEdge.Left;
            largest = left;
        }

        if (right > largest)
        {
            edge = TaskbarEdge.Right;
        }

        return new TaskbarPlacement(edge, work, bounds);
    }

    /// <summary>
    /// Final resting position for a panel of <paramref name="size"/>, anchored near
    /// <paramref name="anchor"/> (the click point on the tray icon) and clamped to stay on screen.
    /// </summary>
    public static Point ShownLocation(TaskbarPlacement placement, Size size, Point anchor, int margin = 8)
    {
        Rectangle work = placement.WorkArea;

        int x;
        int y;

        switch (placement.Edge)
        {
            case TaskbarEdge.Left:
                x = work.Left + margin;
                y = anchor.Y - (size.Height / 2);
                break;

            case TaskbarEdge.Right:
                x = work.Right - size.Width - margin;
                y = anchor.Y - (size.Height / 2);
                break;

            case TaskbarEdge.Top:
                x = anchor.X - (size.Width / 2);
                y = work.Top + margin;
                break;

            default:
                x = anchor.X - (size.Width / 2);
                y = work.Bottom - size.Height - margin;
                break;
        }

        x = Math.Clamp(x, work.Left + margin, Math.Max(work.Left + margin, work.Right - size.Width - margin));
        y = Math.Clamp(y, work.Top + margin, Math.Max(work.Top + margin, work.Bottom - size.Height - margin));

        return new Point(x, y);
    }

    /// <summary>Off-screen start position, just behind the taskbar, that the panel slides out from.</summary>
    public static Point HiddenLocation(TaskbarPlacement placement, Size size, Point shown) => placement.Edge switch
    {
        TaskbarEdge.Left => new Point(placement.ScreenBounds.Left - size.Width, shown.Y),
        TaskbarEdge.Right => new Point(placement.ScreenBounds.Right, shown.Y),
        TaskbarEdge.Top => new Point(shown.X, placement.ScreenBounds.Top - size.Height),
        _ => new Point(shown.X, placement.ScreenBounds.Bottom),
    };
}
