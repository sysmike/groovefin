namespace Groovefin.Core.Navigation;

/// <summary>
/// Brings a list or page back to a remembered offset while its content loads (#284). The offset can be
/// reached only once the content is that long, so each time the scrollable height grows, it goes as far as
/// it can; a list that loads its next page near its end then loads it. It is done when it reaches the
/// offset, when the user scrolls (<see cref="Cancel"/>) or after <see cref="Timeout"/>.
/// </summary>
public sealed class PendingScroll
{
    /// <summary>How long it waits for the content at most.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Offsets this close count as reached (layout rounds to fractions of a pixel).</summary>
    private const double Tolerance = 0.5;

    private readonly double _target;
    private readonly TimeProvider _time;
    private readonly long _started;
    private double _applied;

    public PendingScroll(double target, TimeProvider? time = null)
    {
        _target = target;
        _time = time ?? TimeProvider.System;
        _started = _time.GetTimestamp();
        IsDone = Reached = target <= Tolerance;
    }

    /// <summary>Gets a value indicating whether nothing is left to do.</summary>
    public bool IsDone { get; private set; }

    /// <summary>Gets a value indicating whether the offset was reached (not cancelled or timed out).</summary>
    public bool Reached { get; private set; }

    /// <summary>
    /// Gets the offset to scroll to now that the content can scroll by <paramref name="scrollableHeight"/>,
    /// or <see langword="null"/> to wait for more content.
    /// </summary>
    public double? Next(double scrollableHeight)
    {
        if (IsDone)
        {
            return null;
        }

        if (scrollableHeight >= _target - Tolerance)
        {
            IsDone = Reached = true;
            return _target;
        }

        if (_time.GetElapsedTime(_started) > Timeout)
        {
            IsDone = true;
            return null;
        }

        if (scrollableHeight > _applied + Tolerance)
        {
            _applied = scrollableHeight;
            return scrollableHeight;
        }

        return null;
    }

    /// <summary>Stops: the user scrolled or the page was left.</summary>
    public void Cancel() => IsDone = true;
}
