using System;

namespace Chant.Tracking;

/// <summary>
/// How your casts went since the plugin loaded: how many went off, how many you slid, how many
/// movement cost you, and how close to the edge of the window you started moving.
/// </summary>
public sealed class SessionStats
{
    public int Casts { get; private set; }
    public int Completed { get; private set; }
    public int Interrupted { get; private set; }
    /// <summary>Cancelled casts where you had started moving first.</summary>
    public int MovedTooEarly { get; private set; }
    /// <summary>Completed casts where you were already moving before the bar ended.</summary>
    public int Slides { get; private set; }

    private double leadSum;

    /// <summary>Average time before the end you started moving on a slide, in seconds.</summary>
    public float AverageSlideLead => Slides == 0 ? 0f : (float)(leadSum / Slides);

    /// <summary>
    /// The latest point before the end at which moving still cancelled a cast, when that was inside
    /// your slidecast window: a sign the window is set wider than your connection allows.
    /// </summary>
    public float TightestCancel { get; private set; } = float.NaN;

    public void RecordCompleted(float movedAtRemaining)
    {
        Casts++;
        Completed++;
        if (!float.IsNaN(movedAtRemaining) && movedAtRemaining > 0f)
        {
            Slides++;
            leadSum += movedAtRemaining;
        }
    }

    public void RecordInterrupted(float movedAtRemaining, float slideWindow)
    {
        Casts++;
        Interrupted++;
        if (float.IsNaN(movedAtRemaining)) return;

        MovedTooEarly++;
        if (movedAtRemaining <= slideWindow)
            TightestCancel = float.IsNaN(TightestCancel) ? movedAtRemaining : Math.Min(TightestCancel, movedAtRemaining);
    }

    /// <summary>A slidecast window that would have saved the tightest cancel, or NaN when none is needed.</summary>
    public float SuggestedWindow => float.IsNaN(TightestCancel) ? float.NaN : MathF.Max(0.1f, MathF.Round((TightestCancel - 0.05f) * 20f) / 20f);

    public void Reset()
    {
        Casts = Completed = Interrupted = MovedTooEarly = Slides = 0;
        leadSum = 0;
        TightestCancel = float.NaN;
    }
}
