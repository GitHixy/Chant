using System;
using Chant.Models;
using Chant.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Chant.Tracking;

/// <summary>
/// Follows your own casts, the global cooldown and the action queue each frame, and turns them into
/// a <see cref="CastFrame"/> for the renderer. Read-only: it never touches the game's state.
/// </summary>
public sealed unsafe class CastTracker
{
    /// <summary>The global cooldown's recast group, zero-based (group 58 in the sheets).</summary>
    private const int GcdGroup = 57;

    /// <summary>A cast that ends with this little left counts as gone off.</summary>
    private const float CompleteTolerance = 0.12f;

    private readonly ConfigurationService configService;
    private readonly ActionCatalog catalog;

    private bool casting;
    private ActionType castType;
    private uint castId;
    private ActionInfo castInfo;
    private float lastElapsed;
    private float lastTotal;
    private float movedAtRemaining = float.NaN;
    private bool safeReached;
    private double safeSince;
    private double lastUpdate;

    private CastFrame ended = CastFrame.Empty;
    private double endedAt = double.MinValue;

    public CastFrame Frame { get; private set; } = CastFrame.Empty;
    public SessionStats Stats { get; } = new();

    /// <summary>The info of the cast in progress or last seen, for Diagnostics.</summary>
    public ActionInfo LastInfo => castInfo;
    public (ActionType Type, uint Id) LastAction => (castType, castId);

    /// <summary>Raised once per cast the moment moving becomes safe, with the cast's total time.</summary>
    public event Action<float>? BecameSafe;

    public CastTracker(ConfigurationService configService, ActionCatalog catalog)
    {
        this.configService = configService;
        this.catalog = catalog;
    }

    private Configuration Config => configService.Configuration;

    public void Update(double now, float linger)
    {
        var dt = (float)Math.Clamp(now - lastUpdate, 0.0, 0.25);
        lastUpdate = now;

        var player = Plugin.ObjectTable.LocalPlayer;
        var am = ActionManager.Instance();
        if (player is null || am is null)
        {
            Reset();
            return;
        }

        MovementSense.Update(player.Position);
        var moving = MovementSense.Moving;

        if (player.IsCasting && player.TotalCastTime > 0f)
        {
            var type = (ActionType)player.CastActionType;
            var id = player.CastActionId;
            var elapsed = player.CurrentCastTime;
            var total = player.TotalCastTime;

            // A new cast, or the same spell cast again straight after the last one.
            if (!casting || type != castType || id != castId || elapsed + 0.05f < lastElapsed)
                StartCast(type, id, total);

            casting = true;
            lastElapsed = elapsed;
            lastTotal = total;

            var remaining = total - elapsed;
            var slideAt = Math.Max(total - Config.SlideWindow, 0f);
            if (moving && float.IsNaN(movedAtRemaining))
                movedAtRemaining = remaining;

            if (!safeReached && elapsed >= slideAt)
            {
                safeReached = true;
                safeSince = now;
                BecameSafe?.Invoke(total);
            }

            var frame = new CastFrame
            {
                Phase = CastPhase.Casting,
                Kind = castInfo.Kind,
                Name = castInfo.Name,
                Icon = castInfo.Icon,
                Elapsed = elapsed,
                Total = total,
                SlideAt = slideAt,
                ReadyAt = Math.Max(slideAt - Config.ReadyLead, 0f),
                Moving = moving,
                SinceSafe = safeReached ? (float)(now - safeSince) : -1f,
                MovedEarlyBy = float.NaN,
            };
            FillQueue(ref frame, am, remaining);
            Frame = frame;
            return;
        }

        if (casting)
            EndCast(now, moving, dt);

        if (now - endedAt < linger && ended.Visible)
        {
            var f = ended;
            f.SinceEnd = (float)(now - endedAt);
            f.SinceSafe = f.Phase == CastPhase.Completed ? f.SinceSafe + f.SinceEnd : -1f;
            f.Moving = moving;
            FillQueue(ref f, am, 0f);
            Frame = f;
            return;
        }

        Frame = Config.ShowGcd ? GcdFrame(am, moving) : CastFrame.Empty;
    }

    private void StartCast(ActionType type, uint id, float total)
    {
        castType = type;
        castId = id;
        castInfo = catalog.Get(type, id);
        movedAtRemaining = float.NaN;
        safeReached = false;
        lastElapsed = 0f;

        if (Config.LogEvents)
            Plugin.Log.Info("Cast {Name} ({Type} {Id}): {Kind} ({Why}), {Total:0.00}s",
                castInfo.Name, type, id, castInfo.Kind, castInfo.Why, total);
    }

    private void EndCast(double now, bool movingNow, float dt)
    {
        casting = false;
        var remaining = Math.Max(lastTotal - lastElapsed, 0f);
        var completed = remaining <= Math.Max(CompleteTolerance, dt * 2f);

        // Movement often registers on the very frame the cast is cancelled.
        if (!completed && movingNow && float.IsNaN(movedAtRemaining))
            movedAtRemaining = remaining;

        var window = Config.SlideWindow;
        var frame = new CastFrame
        {
            Phase = completed ? CastPhase.Completed : CastPhase.Interrupted,
            Kind = castInfo.Kind,
            Name = castInfo.Name,
            Icon = castInfo.Icon,
            Elapsed = completed ? lastTotal : lastElapsed,
            Total = lastTotal,
            SlideAt = Math.Max(lastTotal - window, 0f),
            ReadyAt = Math.Max(lastTotal - window - Config.ReadyLead, 0f),
            SinceSafe = safeReached ? (float)(now - safeSince) : -1f,
            QueueAt = float.NaN,
            MovedEarlyBy = !completed && !float.IsNaN(movedAtRemaining) ? movedAtRemaining - window : float.NaN,
        };

        if (completed)
            Stats.RecordCompleted(movedAtRemaining);
        else
            Stats.RecordInterrupted(movedAtRemaining, window);

        if (Config.LogEvents)
        {
            if (completed)
                Plugin.Log.Info("Cast {Name} went off{Slide}", castInfo.Name,
                    float.IsNaN(movedAtRemaining) ? string.Empty : $", moving from {movedAtRemaining:0.00}s before the end");
            else
                Plugin.Log.Info("Cast {Name} cancelled with {Remaining:0.00}s left{Moved}", castInfo.Name, remaining,
                    float.IsNaN(movedAtRemaining) ? " (no movement seen)" : $", moved {movedAtRemaining:0.00}s before the end");
        }

        ended = frame;
        endedAt = now;
    }

    /// <summary>
    /// Where the queue window opens: once nothing blocks you for longer than the queue window,
    /// whether that's the cast itself, the global cooldown or an animation lock.
    /// </summary>
    private void FillQueue(ref CastFrame frame, ActionManager* am, float castRemaining)
    {
        var block = Math.Max(castRemaining, am->AnimationLock);
        var gcd = am->GetRecastGroupDetail(GcdGroup);
        if (gcd is not null && gcd->IsActive && gcd->Total > 0f)
            block = Math.Max(block, gcd->Total - gcd->Elapsed);

        var window = Config.QueueWindow;
        frame.QueueOpen = block > 0f && block <= window;

        if (frame.Phase is CastPhase.Casting or CastPhase.Gcd)
        {
            var at = frame.Elapsed + block - window;
            frame.QueueAt = at <= frame.Total ? Math.Max(at, 0f) : float.NaN;
        }
        else
        {
            frame.QueueAt = float.NaN;
        }

        frame.QueuedIcon = 0;
        if (am->ActionQueued && am->QueuedActionId != 0)
        {
            var id = am->QueuedActionType == ActionType.Action ? am->GetAdjustedActionId(am->QueuedActionId) : am->QueuedActionId;
            frame.QueuedIcon = catalog.Get(am->QueuedActionType, id).Icon;
        }
    }

    /// <summary>The global cooldown rolling after an instant action, shown on the same shape.</summary>
    private CastFrame GcdFrame(ActionManager* am, bool moving)
    {
        var gcd = am->GetRecastGroupDetail(GcdGroup);
        if (gcd is null || !gcd->IsActive || gcd->Total <= 0f || gcd->Elapsed >= gcd->Total)
            return CastFrame.Empty;

        var info = catalog.Get(ActionType.Action, gcd->ActionId);
        var frame = new CastFrame
        {
            Phase = CastPhase.Gcd,
            Kind = info.Kind,
            Name = info.Name,
            Icon = info.Icon,
            Elapsed = gcd->Elapsed,
            Total = gcd->Total,
            SlideAt = 0f,
            ReadyAt = 0f,
            Moving = moving,
            SinceSafe = -1f,
            MovedEarlyBy = float.NaN,
        };
        FillQueue(ref frame, am, 0f);
        return frame;
    }

    public void Reset()
    {
        casting = false;
        ended = CastFrame.Empty;
        endedAt = double.MinValue;
        Frame = CastFrame.Empty;
        MovementSense.Reset();
    }
}
