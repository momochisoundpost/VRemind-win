namespace VRemindWin.Core.Services;

/// <summary>
/// Tracks consecutive diagnostic taps within a short sliding timeout window.
/// </summary>
public sealed class CalibrationTapAccumulator
{
    private readonly int _requiredTaps;
    private readonly TimeSpan _maxGap;
    private DateTimeOffset? _lastTapUtc;

    public CalibrationTapAccumulator(int requiredTaps = 8, TimeSpan? maxGap = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredTaps, 1);
        _requiredTaps = requiredTaps;
        _maxGap = maxGap ?? TimeSpan.FromSeconds(3);
    }

    public int CurrentCount { get; private set; }

    public bool RegisterTap(DateTimeOffset utcNow)
    {
        if (_lastTapUtc is not DateTimeOffset lastTap)
        {
            CurrentCount = 1;
        }
        else
        {
            var elapsed = utcNow - lastTap;
            CurrentCount = elapsed < TimeSpan.Zero || elapsed > _maxGap
                ? 1
                : CurrentCount + 1;
        }

        _lastTapUtc = utcNow;

        if (CurrentCount >= _requiredTaps)
        {
            Reset();
            return true;
        }

        return false;
    }

    public void Reset()
    {
        CurrentCount = 0;
        _lastTapUtc = null;
    }
}
