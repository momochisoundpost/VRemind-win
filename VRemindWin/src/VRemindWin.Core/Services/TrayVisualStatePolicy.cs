using VRemindWin.Core.Models;

namespace VRemindWin.Core.Services;

public enum TrayVisualState
{
    Normal,
    Live,
    Gta6Live
}

/// <summary>
/// Resolves the system tray visual state and tooltip text from the current CLI state.
/// </summary>
public static class TrayVisualStatePolicy
{
    public static TrayVisualState Resolve(CliStateEnvelope state)
    {
        if (state is null)
        {
            return TrayVisualState.Normal;
        }

        if (state.Sections.Live.Any(c => c.IsGta6Live))
        {
            return TrayVisualState.Gta6Live;
        }

        return state.LiveCount > 0 ? TrayVisualState.Live : TrayVisualState.Normal;
    }

    public static string TooltipFor(TrayVisualState state)
        => state switch
        {
            TrayVisualState.Gta6Live => "VRemind (Live now - GTA 6)",
            TrayVisualState.Live => "VRemind (Live now)",
            _ => "VRemind"
        };
}
