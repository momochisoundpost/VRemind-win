namespace VRemindWin.Core.Services;

/// <summary>
/// Computes DPI-aware screen coordinates for the tray flyout window so it stays anchored
/// near the notification icon and inside the active monitor's work area on Windows 10/11.
/// </summary>
public static class TrayFlyoutPositioner
{
    public static (int X, int Y, int Width, int Height) CalculateFlyoutBounds(
        (int Left, int Top, int Right, int Bottom) iconRect,
        (int Left, int Top, int Right, int Bottom) workArea,
        uint dpi,
        int logicalWidth = 360,
        int logicalHeight = 440,
        int marginDip = 8)
    {
        double effectiveDpi = dpi == 0 ? 96.0 : dpi;
        double scale = effectiveDpi / 96.0;

        int workWidth = Math.Max(1, workArea.Right - workArea.Left);
        int workHeight = Math.Max(1, workArea.Bottom - workArea.Top);

        int physicalWidth = Math.Min(workWidth, Math.Max(1, (int)Math.Round(logicalWidth * scale)));
        int physicalHeight = Math.Min(workHeight, Math.Max(1, (int)Math.Round(logicalHeight * scale)));
        int margin = Math.Max(0, (int)Math.Round(marginDip * scale));

        int minX = workWidth >= physicalWidth + (margin * 2) ? workArea.Left + margin : workArea.Left;
        int maxX = Math.Max(minX, (workWidth >= physicalWidth + (margin * 2) ? workArea.Right - physicalWidth - margin : workArea.Right - physicalWidth));
        int minY = workHeight >= physicalHeight + (margin * 2) ? workArea.Top + margin : workArea.Top;
        int maxY = Math.Max(minY, (workHeight >= physicalHeight + (margin * 2) ? workArea.Bottom - physicalHeight - margin : workArea.Bottom - physicalHeight));

        int iconCenterX = iconRect.Left + ((iconRect.Right - iconRect.Left) / 2);
        int iconCenterY = iconRect.Top + ((iconRect.Bottom - iconRect.Top) / 2);

        int x;
        int y;

        if (iconRect.Top >= workArea.Bottom)
        {
            // Bottom taskbar
            x = iconCenterX - (physicalWidth / 2);
            y = workArea.Bottom - physicalHeight - margin;
        }
        else if (iconRect.Bottom <= workArea.Top)
        {
            // Top taskbar
            x = iconCenterX - (physicalWidth / 2);
            y = workArea.Top + margin;
        }
        else if (iconRect.Left >= workArea.Right)
        {
            // Right taskbar
            x = workArea.Right - physicalWidth - margin;
            y = iconCenterY - (physicalHeight / 2);
        }
        else if (iconRect.Right <= workArea.Left)
        {
            // Left taskbar
            x = workArea.Left + margin;
            y = iconCenterY - (physicalHeight / 2);
        }
        else
        {
            // Auto-hide or unknown icon position: anchor near bottom-right of work area.
            x = maxX;
            y = maxY;
        }

        x = Math.Clamp(x, minX, maxX);
        y = Math.Clamp(y, minY, maxY);

        return (x, y, physicalWidth, physicalHeight);
    }

    public static bool ShouldSuppressTrayReopen(
        DateTimeOffset? lastDeactivatedUtc,
        DateTimeOffset nowUtc,
        TimeSpan? cooldown = null)
    {
        if (lastDeactivatedUtc is not { } deactivatedAt)
        {
            return false;
        }

        var elapsed = nowUtc - deactivatedAt;
        return elapsed >= TimeSpan.Zero && elapsed <= (cooldown ?? TimeSpan.FromMilliseconds(300));
    }
}
