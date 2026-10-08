using System;
using Chant.Models;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Chant.Tracking;

/// <summary>
/// A scripted loop of sample casts - a slidecast, a cast held still, a raise cancelled by moving too
/// early, an instant with its global cooldown, a mount - used by /chant test and the settings preview.
/// It's a pure function of time, so the preview and the world always agree.
/// </summary>
public static class TestCasts
{
    private const float GcdLength = 2.5f;
    private const float Gap = 0.7f;

    /// <param name="MoveAt">Seconds before the end at which the stand-in starts moving, or NaN to stay still.</param>
    /// <param name="QueueId">Action queued near the end, or 0.</param>
    private readonly record struct Step(ActionType Type, uint Id, float Cast, float MoveAt, uint QueueId);

    private static readonly Step[] Script =
    [
        new(ActionType.Action, 152, 3.0f, 0.30f, 3577),          // Fire III, slid, Fire IV queued
        new(ActionType.Action, 135, 2.0f, float.NaN, 133),       // Cure II held still, Medica II queued
        new(ActionType.Action, 125, 8.0f, 1.10f, 0),             // Raise, moved too early
        new(ActionType.Action, 16532, 0f, float.NaN, 0),         // Dia, an instant: just the global cooldown
        new(ActionType.Action, 25798, 1.5f, float.NaN, 0),       // Summon Carbuncle
        new(ActionType.Mount, 1, 1.0f, float.NaN, 0),            // Company chocobo
    ];

    public static CastFrame Evaluate(double time, Configuration config, ActionCatalog catalog, float linger)
    {
        var cycle = 0f;
        foreach (var step in Script)
            cycle += Duration(step, config, linger);

        var t = (float)(time % cycle);
        foreach (var step in Script)
        {
            var d = Duration(step, config, linger);
            if (t < d)
                return Frame(step, t, config, catalog, linger);
            t -= d;
        }

        return CastFrame.Empty;
    }

    private static bool Interrupted(Step s, Configuration config) => !float.IsNaN(s.MoveAt) && s.MoveAt > config.SlideWindow;

    private static float EndOf(Step s, Configuration config) => Interrupted(s, config) ? s.Cast - s.MoveAt : s.Cast;

    private static float Duration(Step s, Configuration config, float linger)
        => (s.Cast <= 0f ? GcdLength : EndOf(s, config) + linger) + Gap;

    private static CastFrame Frame(Step s, float t, Configuration config, ActionCatalog catalog, float linger)
    {
        var info = catalog.Get(s.Type, s.Id);

        if (s.Cast <= 0f)
        {
            if (t >= GcdLength) return CastFrame.Empty;
            var queueAt = GcdLength - config.QueueWindow;
            return new CastFrame
            {
                Phase = CastPhase.Gcd,
                Kind = info.Kind,
                Name = info.Name,
                Icon = info.Icon,
                Elapsed = t,
                Total = GcdLength,
                QueueAt = queueAt,
                QueueOpen = t >= queueAt,
                SinceSafe = -1f,
                MovedEarlyBy = float.NaN,
                IsTest = true,
            };
        }

        var slideAt = Math.Max(s.Cast - config.SlideWindow, 0f);
        var end = EndOf(s, config);
        var interrupted = Interrupted(s, config);
        var moveFrom = float.IsNaN(s.MoveAt) ? float.MaxValue : s.Cast - s.MoveAt;

        var frame = new CastFrame
        {
            Kind = info.Kind,
            Name = info.Name,
            Icon = info.Icon,
            Total = s.Cast,
            SlideAt = slideAt,
            ReadyAt = Math.Max(slideAt - config.ReadyLead, 0f),
            QueueAt = Math.Max(s.Cast - config.QueueWindow, 0f),
            MovedEarlyBy = float.NaN,
            IsTest = true,
        };

        if (t < end)
        {
            frame.Phase = CastPhase.Casting;
            frame.Elapsed = t;
            frame.Moving = t >= moveFrom;
            frame.SinceSafe = t >= slideAt ? t - slideAt : -1f;
            frame.QueueOpen = t >= frame.QueueAt;
            frame.QueuedIcon = s.QueueId != 0 && t >= s.Cast - 0.4f ? catalog.Get(ActionType.Action, s.QueueId).Icon : 0;
            return frame;
        }

        if (t < end + linger)
        {
            frame.Phase = interrupted ? CastPhase.Interrupted : CastPhase.Completed;
            frame.Elapsed = end;
            frame.SinceEnd = t - end;
            frame.SinceSafe = interrupted ? -1f : t - slideAt;
            frame.QueueAt = float.NaN;
            frame.Moving = !interrupted && t >= moveFrom;
            frame.MovedEarlyBy = interrupted ? s.MoveAt - config.SlideWindow : float.NaN;
            return frame;
        }

        return CastFrame.Empty;
    }
}
