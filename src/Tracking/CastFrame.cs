using System;
using Chant.Models;

namespace Chant.Tracking;

public enum CastPhase
{
    None,
    /// <summary>A cast in progress.</summary>
    Casting,
    /// <summary>No cast, but the global cooldown is rolling after an instant action.</summary>
    Gcd,
    /// <summary>A cast that just went off, lingering briefly.</summary>
    Completed,
    /// <summary>A cast that was cancelled, lingering briefly.</summary>
    Interrupted,
}

/// <summary>
/// Everything the renderer needs for one frame of the castbar. Times are in seconds from the start
/// of the cast (or of the global cooldown).
/// </summary>
public struct CastFrame
{
    public CastPhase Phase;
    public ActionKind Kind;
    public string Name;
    public uint Icon;

    public float Elapsed;
    public float Total;

    /// <summary>From here on, moving no longer cancels the cast.</summary>
    public float SlideAt;
    /// <summary>From here on, the bar tells you to get ready to move.</summary>
    public float ReadyAt;
    /// <summary>Where the queue window opens on this bar, or NaN when it opens after the bar ends.</summary>
    public float QueueAt;
    /// <summary>The game would accept a queued action right now.</summary>
    public bool QueueOpen;
    /// <summary>Icon of the action already queued, or 0.</summary>
    public uint QueuedIcon;

    /// <summary>You're moving, or pressing a movement key or stick, this frame.</summary>
    public bool Moving;

    /// <summary>Seconds since the cast ended, for the linger animations.</summary>
    public float SinceEnd;
    /// <summary>Seconds since it became safe to move, or a negative number before that.</summary>
    public float SinceSafe;
    /// <summary>For an interrupted cast: how much too early you moved, or NaN when movement wasn't the cause.</summary>
    public float MovedEarlyBy;

    public bool IsTest;

    public readonly bool Visible => Phase != CastPhase.None;
    public readonly float Progress => Total <= 0f ? 1f : Math.Clamp(Elapsed / Total, 0f, 1f);
    public readonly float Remaining => Math.Max(Total - Elapsed, 0f);

    /// <summary>Moving now would not cancel anything.</summary>
    public readonly bool Safe => Phase != CastPhase.Casting || Elapsed >= SlideAt;
    public readonly bool Ready => Phase == CastPhase.Casting && Elapsed >= ReadyAt && Elapsed < SlideAt;
    /// <summary>Moving while it isn't safe: the cast is about to be cancelled.</summary>
    public readonly bool Danger => Phase == CastPhase.Casting && Moving && !Safe;

    public static readonly CastFrame Empty = new() { Phase = CastPhase.None, Name = string.Empty, QueueAt = float.NaN, MovedEarlyBy = float.NaN };
}
