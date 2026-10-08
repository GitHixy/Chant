using System;
using System.Globalization;
using System.Numerics;
using Chant.Models;
using Chant.Services;
using Chant.Tracking;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace Chant.Rendering;

/// <summary>Per-frame inputs that don't belong to the preset.</summary>
public struct RenderContext
{
    /// <summary>Screen point the castbar is centred on, before the preset's offset.</summary>
    public Vector2 Anchor;
    /// <summary>Resolution and global scale; the preset's own scale is applied on top.</summary>
    public float Scale;
    public float Opacity;
    public float Now;
    public float Linger;
    public bool ReducedMotion;
    public bool MoveWarning;
    public bool Coach;
}

/// <summary>
/// Draws one <see cref="CastFrame"/> in the shape a <see cref="Preset"/> describes: a straight bar
/// filling in any direction, or a ring from a full circle down to a shallow arc. Everything is drawn
/// with ImGui primitives on the draw list it's handed, so the same code paints the world overlay
/// and the preview in the settings.
/// </summary>
public sealed class CastRenderer
{
    private const float Tau = MathF.PI * 2f;

    /// <summary>The global cooldown is drawn this much thinner than a cast, so the two never look alike.</summary>
    private const float GcdThickness = 0.4f;

    private readonly FontService fonts;

    /// <summary>Screen rectangle covered by the last frame drawn, icons included.</summary>
    public Vector2 BoundsMin { get; private set; }
    public Vector2 BoundsMax { get; private set; }

    public CastRenderer(FontService fonts)
    {
        this.fonts = fonts;
    }

    // Per-draw state, so the helpers don't need a dozen parameters each.
    private ImDrawListPtr dl;
    private Preset p = null!;
    private CastFrame f;
    private RenderContext ctx;
    private float s;
    private float alpha;
    private Vector2 center;

    private Vector4 stateColor;
    private Vector4 fillColor;
    private Vector4 frameColor;
    private float glowBoost;

    public void Draw(ImDrawListPtr drawList, in CastFrame frame, Preset preset, in RenderContext context)
    {
        if (!frame.Visible) return;

        dl = drawList;
        p = preset;
        f = frame;
        ctx = context;

        alpha = Math.Clamp(ctx.Opacity, 0f, 1f);
        var swell = 1f;

        if (f.Phase is CastPhase.Completed or CastPhase.Interrupted)
        {
            var k = Math.Clamp(f.SinceEnd / Math.Max(ctx.Linger, 0.01f), 0f, 1f);
            alpha *= 1f - SmoothStep(0.3f, 1f, k);
            if (f.Phase == CastPhase.Completed && !ctx.ReducedMotion)
                swell += 0.05f * EaseOut(k);
        }

        if (p.PulseOnSafe && !ctx.ReducedMotion && f.SinceSafe is >= 0f and < 0.35f && f.Phase == CastPhase.Casting)
            swell += 0.09f * MathF.Pow(1f - f.SinceSafe / 0.35f, 2f);

        if (alpha <= 0.003f) return;

        s = ctx.Scale * p.Scale * swell;
        center = ctx.Anchor + p.Offset * ctx.Scale;

        if (p.ShakeOnDanger && !ctx.ReducedMotion && ctx.MoveWarning && f.Danger)
            center += new Vector2(MathF.Sin(ctx.Now * 83f), MathF.Cos(ctx.Now * 71f)) * 3f * ctx.Scale;

        PickColors();

        if (p.Shape == CastShape.Ring)
            DrawRing();
        else
            DrawBar();
    }

    // ------------------------------------------------------------------
    // Colours
    // ------------------------------------------------------------------

    private void PickColors()
    {
        var c = p.Colors;
        var kind = c.For(f.Kind);
        var casting = f.Phase == CastPhase.Casting;
        var danger = casting && ctx.MoveWarning && f.Danger;
        glowBoost = 0f;

        if (f.Phase == CastPhase.Interrupted)
            stateColor = c.Danger;
        else if (danger)
            stateColor = c.Danger;
        else if (casting && !f.Safe)
            stateColor = f.Ready ? c.Ready : c.Lock;
        else if (f.Phase == CastPhase.Gcd)
            stateColor = c.Gcd;
        else
            stateColor = c.Safe;

        fillColor = f.Phase switch
        {
            CastPhase.Casting when !f.Safe => Vector4.Lerp(kind, f.Ready ? c.Ready : c.Lock, p.LockTint),
            // On the global cooldown, the open queue window shows as the fill turning to the queue colour.
            CastPhase.Gcd => f.QueueOpen ? c.Queue : c.Gcd,
            CastPhase.Interrupted => Vector4.Lerp(Grey(kind), c.Danger, 0.45f),
            _ => kind,
        };

        frameColor = p.LockFrame && f.Phase != CastPhase.Gcd ? stateColor : c.Frame;

        if (danger)
        {
            var blink = 0.5f + 0.5f * MathF.Sin(ctx.Now * 22f);
            fillColor = Vector4.Lerp(fillColor, c.Danger, 0.35f + 0.35f * blink);
            glowBoost = 0.6f + 0.4f * blink;
        }
        else if (casting && f.SinceSafe is >= 0f and < 0.4f && p.PulseOnSafe)
        {
            glowBoost = 1f - f.SinceSafe / 0.4f;
        }
    }

    private static Vector4 Grey(Vector4 c)
    {
        var l = c.X * 0.3f + c.Y * 0.59f + c.Z * 0.11f;
        return new Vector4(l, l, l, c.W);
    }

    private bool IsGcd => f.Phase == CastPhase.Gcd;

    private uint Col(Vector4 c, float a = 1f) => ImGui.GetColorU32(c with { W = Math.Clamp(c.W * a * alpha, 0f, 1f) });

    // ------------------------------------------------------------------
    // Bar
    // ------------------------------------------------------------------

    private Vector2 barMin;
    private Vector2 barMax;

    private void DrawBar()
    {
        var vertical = p.IsVertical;
        var thickness = p.Thickness * (IsGcd ? GcdThickness : 1f);
        var size = vertical ? new Vector2(thickness, p.Length) * s : new Vector2(p.Length, thickness) * s;
        barMin = center - size * 0.5f;
        barMax = center + size * 0.5f;
        var thick = (vertical ? size.X : size.Y);
        var rounding = Math.Min(p.Rounding * s, thick * 0.5f);

        // Glow
        var glow = IsGcd ? 0f : Math.Clamp(p.Glow + glowBoost * 0.6f, 0f, 1.6f);
        if (glow > 0.01f)
        {
            for (var i = 1; i <= 4; i++)
            {
                var pad = i * 2.2f * s * (0.6f + glow * 0.5f);
                dl.AddRect(barMin - new Vector2(pad), barMax + new Vector2(pad), Col(stateColor, glow * 0.16f / i),
                    rounding + pad, ImDrawFlags.None, 2.4f * s);
            }
        }

        // Track
        dl.AddRectFilled(barMin, barMax, Col(p.Colors.Background), rounding);

        var casting = f.Phase == CastPhase.Casting;
        if (p.ShowSlideZone && f.Total > 0f && f.Phase is CastPhase.Casting or CastPhase.Completed)
        {
            var (rMin, rMax) = BarSpan(f.ReadyAt / f.Total, f.SlideAt / f.Total);
            if (rMax.X - rMin.X > 0.5f && rMax.Y - rMin.Y > 0.5f)
                dl.AddRectFilled(rMin, rMax, Col(p.Colors.Ready, 0.14f), rounding);
            var (zMin, zMax) = BarSpan(f.SlideAt / f.Total, 1f);
            var zoneAlpha = casting && f.Safe ? 0.22f : 0.34f;
            dl.AddRectFilled(zMin, zMax, Col(p.Colors.Safe, zoneAlpha), rounding);
            // A hard edge where it opens, so it reads at a glance.
            var (eMin, eMax) = BarSpan(f.SlideAt / f.Total, f.SlideAt / f.Total);
            if (vertical)
                dl.AddLine(new Vector2(eMin.X, eMin.Y), new Vector2(eMax.X, eMin.Y), Col(p.Colors.Safe, 0.85f), Math.Max(1.2f * s, 1f));
            else
                dl.AddLine(new Vector2(eMin.X, eMin.Y), new Vector2(eMin.X, eMax.Y), Col(p.Colors.Safe, 0.85f), Math.Max(1.2f * s, 1f));
        }

        // Fill
        var progress = f.Progress;
        if (progress > 0f)
        {
            var (fMin, fMax) = BarSpan(0f, progress);
            var len = vertical ? fMax.Y - fMin.Y : fMax.X - fMin.X;
            var r = Math.Min(rounding, len * 0.5f);
            dl.AddRectFilled(fMin, fMax, Col(fillColor), r);

            // A sheen along the top (or left) of the fill, and a bright leading edge.
            var sheen = vertical ? new Vector2(fMin.X + (fMax.X - fMin.X) * 0.45f, fMax.Y) : new Vector2(fMax.X, fMin.Y + (fMax.Y - fMin.Y) * 0.45f);
            dl.AddRectFilled(fMin, sheen, Col(Vector4.One, 0.10f), r);

            var (hMin, hMax) = BarSpan(progress, progress);
            var edge = Math.Max(1.5f * s, 1f);
            if (vertical)
                dl.AddRectFilled(new Vector2(hMin.X, hMin.Y - edge), new Vector2(hMax.X, hMin.Y + edge), Col(Vector4.One, 0.55f));
            else
                dl.AddRectFilled(new Vector2(hMin.X - edge, hMin.Y), new Vector2(hMin.X + edge, hMax.Y), Col(Vector4.One, 0.55f));

            if (f.Phase == CastPhase.Completed)
                dl.AddRectFilled(fMin, fMax, Col(Vector4.One, 0.55f * (1f - Math.Clamp(f.SinceEnd / 0.25f, 0f, 1f))), r);
        }

        // Ticks every half second, for counting the rhythm of long casts.
        if (p.ShowTicks && f.Total is > 0f and <= 30f)
        {
            for (var t = 0.5f; t < f.Total - 0.05f; t += 0.5f)
            {
                var whole = MathF.Abs(t - MathF.Round(t)) < 0.01f;
                var (a, b) = BarSpan(t / f.Total, t / f.Total);
                var depth = (whole ? 0.45f : 0.25f) * thick;
                if (vertical)
                    dl.AddLine(new Vector2(a.X, a.Y), new Vector2(a.X + depth, a.Y), Col(Vector4.One, 0.35f), 1f);
                else
                    dl.AddLine(new Vector2(a.X, b.Y - depth), new Vector2(a.X, b.Y), Col(Vector4.One, 0.35f), 1f);
            }
        }

        // Queue marker
        if (p.ShowQueueMarker && !IsGcd && !float.IsNaN(f.QueueAt) && f.Total > 0f)
        {
            var (a, b) = BarSpan(f.QueueAt / f.Total, f.QueueAt / f.Total);
            var over = 3f * s;
            var w = (f.QueueOpen ? 2.2f : 1.4f) * s;
            var qa = f.QueueOpen ? 1f : 0.65f;
            if (vertical)
                dl.AddLine(new Vector2(a.X - over, a.Y), new Vector2(b.X + over, a.Y), Col(p.Colors.Queue, qa), w);
            else
                dl.AddLine(new Vector2(a.X, a.Y - over), new Vector2(a.X, b.Y + over), Col(p.Colors.Queue, qa), w);
        }

        // Frame
        if (p.FrameThickness > 0.01f)
            dl.AddRect(barMin, barMax, Col(frameColor, 0.95f), rounding, ImDrawFlags.None, Math.Max(p.FrameThickness * s, 1f));

        // Icon, text, queued action
        var boundsMin = barMin;
        var boundsMax = barMax;
        var iconSize = p.IconSize * s;
        var gap = 6f * s;
        Vector2 endCursor;

        if (vertical)
        {
            endCursor = new Vector2(center.X, barMin.Y - gap);
            if (p.Icon == IconPlace.Start)
            {
                var c = new Vector2(center.X, barMax.Y + gap + iconSize * 0.5f);
                DrawIcon(c, iconSize, false);
                boundsMax.Y = c.Y + iconSize * 0.5f;
            }
            else if (p.Icon == IconPlace.End)
            {
                var c = new Vector2(center.X, barMin.Y - gap - iconSize * 0.5f);
                DrawIcon(c, iconSize, false);
                boundsMin.Y = c.Y - iconSize * 0.5f;
                endCursor.Y = boundsMin.Y - gap;
            }
            else if (p.Icon == IconPlace.Center)
            {
                DrawIcon(center, iconSize, false);
            }
            boundsMin.X = Math.Min(boundsMin.X, center.X - iconSize * 0.5f);
            boundsMax.X = Math.Max(boundsMax.X, center.X + iconSize * 0.5f);
            if (p.Icon == IconPlace.Hidden)
            {
                boundsMin.X = barMin.X;
                boundsMax.X = barMax.X;
            }
        }
        else
        {
            endCursor = new Vector2(barMax.X + gap, center.Y);
            if (p.Icon == IconPlace.Start)
            {
                var c = new Vector2(barMin.X - gap - iconSize * 0.5f, center.Y);
                DrawIcon(c, iconSize, false);
                boundsMin.X = c.X - iconSize * 0.5f;
            }
            else if (p.Icon == IconPlace.End)
            {
                var c = new Vector2(barMax.X + gap + iconSize * 0.5f, center.Y);
                DrawIcon(c, iconSize, false);
                boundsMax.X = c.X + iconSize * 0.5f;
                endCursor.X = boundsMax.X + gap;
            }
            else if (p.Icon == IconPlace.Center)
            {
                DrawIcon(center, iconSize, false);
            }

            if (p.Icon != IconPlace.Hidden)
            {
                boundsMin.Y = Math.Min(boundsMin.Y, center.Y - iconSize * 0.5f);
                boundsMax.Y = Math.Max(boundsMax.Y, center.Y + iconSize * 0.5f);
            }
        }

        DrawQueued(endCursor, vertical ? new Vector2(0, -1) : new Vector2(1, 0), ref boundsMin, ref boundsMax);
        DrawTexts(barMin, barMax, horizontalBar: !vertical, ref boundsMin, ref boundsMax);

        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }

    /// <summary>The part of the bar between two fractions of the fill, as a rectangle.</summary>
    private (Vector2 Min, Vector2 Max) BarSpan(float u0, float u1)
    {
        u0 = Math.Clamp(u0, 0f, 1f);
        u1 = Math.Clamp(u1, 0f, 1f);
        var w = barMax.X - barMin.X;
        var h = barMax.Y - barMin.Y;
        return p.Direction switch
        {
            FillDirection.RightToLeft => (new Vector2(barMax.X - u1 * w, barMin.Y), new Vector2(barMax.X - u0 * w, barMax.Y)),
            FillDirection.BottomToTop => (new Vector2(barMin.X, barMax.Y - u1 * h), new Vector2(barMax.X, barMax.Y - u0 * h)),
            FillDirection.TopToBottom => (new Vector2(barMin.X, barMin.Y + u0 * h), new Vector2(barMax.X, barMin.Y + u1 * h)),
            _ => (new Vector2(barMin.X + u0 * w, barMin.Y), new Vector2(barMin.X + u1 * w, barMax.Y)),
        };
    }

    // ------------------------------------------------------------------
    // Ring
    // ------------------------------------------------------------------

    private float ringR;
    private float ringW;

    private void DrawRing()
    {
        ringR = p.RingRadius * s;
        ringW = Math.Max(p.RingThickness * s * (IsGcd ? GcdThickness : 1f), 1f);
        var full = p.RingSweep >= 359.5f;
        var roundCaps = p.Rounding > 0.5f && !full;

        // Glow
        var glow = IsGcd ? 0f : Math.Clamp(p.Glow + glowBoost * 0.6f, 0f, 1.6f);
        if (glow > 0.01f)
        {
            for (var i = 1; i <= 4; i++)
                Arc(0f, 1f, ringR, ringW + i * 4.5f * s * (0.6f + glow * 0.5f), Col(stateColor, glow * 0.14f / i), full);
        }

        // Track
        Arc(0f, 1f, ringR, ringW, Col(p.Colors.Background), full);
        if (roundCaps)
        {
            dl.AddCircleFilled(RingPoint(0f, ringR), ringW * 0.5f, Col(p.Colors.Background), 16);
            dl.AddCircleFilled(RingPoint(1f, ringR), ringW * 0.5f, Col(p.Colors.Background), 16);
        }

        var casting = f.Phase == CastPhase.Casting;
        if (p.ShowSlideZone && f.Total > 0f && f.Phase is CastPhase.Casting or CastPhase.Completed)
        {
            Arc(f.ReadyAt / f.Total, f.SlideAt / f.Total, ringR, ringW, Col(p.Colors.Ready, 0.16f), false);
            Arc(f.SlideAt / f.Total, 1f, ringR, ringW, Col(p.Colors.Safe, casting && f.Safe ? 0.24f : 0.36f), false);
            Tick(f.SlideAt / f.Total, ringW * 0.5f + 1.5f * s, Col(p.Colors.Safe, 0.9f), Math.Max(1.4f * s, 1f));
        }

        // Fill
        var progress = f.Progress;
        if (progress > 0f)
        {
            Arc(0f, progress, ringR, ringW, Col(fillColor), full && progress >= 0.999f);
            Arc(0f, progress, ringR + ringW * 0.22f, ringW * 0.3f, Col(Vector4.One, 0.10f), false);
            if (roundCaps)
            {
                dl.AddCircleFilled(RingPoint(0f, ringR), ringW * 0.5f, Col(fillColor), 16);
                dl.AddCircleFilled(RingPoint(progress, ringR), ringW * 0.5f, Col(fillColor), 16);
            }

            if (progress < 0.999f)
            {
                // A bright head on the leading end.
                dl.AddCircleFilled(RingPoint(progress, ringR), ringW * 0.42f, Col(Vector4.One, 0.6f), 12);
            }

            if (f.Phase == CastPhase.Completed)
                Arc(0f, 1f, ringR, ringW, Col(Vector4.One, 0.55f * (1f - Math.Clamp(f.SinceEnd / 0.25f, 0f, 1f))), full);
        }

        if (p.ShowTicks && f.Total is > 0f and <= 30f)
        {
            for (var t = 0.5f; t < f.Total - 0.05f; t += 0.5f)
            {
                var whole = MathF.Abs(t - MathF.Round(t)) < 0.01f;
                Tick(t / f.Total, ringW * (whole ? 0.5f : 0.3f), Col(Vector4.One, 0.35f), 1f);
            }
        }

        if (p.ShowQueueMarker && !IsGcd && !float.IsNaN(f.QueueAt) && f.Total > 0f)
            Tick(f.QueueAt / f.Total, ringW * 0.5f + 3f * s, Col(p.Colors.Queue, f.QueueOpen ? 1f : 0.65f), (f.QueueOpen ? 2.2f : 1.4f) * s);

        // Frame: thin edges inside and outside the band.
        if (p.FrameThickness > 0.01f)
        {
            var t = Math.Max(p.FrameThickness * s * 0.8f, 1f);
            Arc(0f, 1f, ringR + ringW * 0.5f + t * 0.5f, t, Col(frameColor, 0.9f), full);
            Arc(0f, 1f, ringR - ringW * 0.5f - t * 0.5f, t, Col(frameColor, 0.6f), full);
        }

        // Bounds of the arc as drawn, for text placement.
        var outer = ringR + ringW * 0.5f + 2f * s;
        var boundsMin = new Vector2(float.MaxValue);
        var boundsMax = new Vector2(float.MinValue);
        for (var i = 0; i <= 32; i++)
        {
            var pt = RingPoint(i / 32f, outer);
            var inner = RingPoint(i / 32f, Math.Max(ringR - ringW * 0.5f, 0f));
            boundsMin = Vector2.Min(boundsMin, Vector2.Min(pt, inner));
            boundsMax = Vector2.Max(boundsMax, Vector2.Max(pt, inner));
        }

        var iconSize = p.IconSize * s;
        var gap = 6f * s;
        var endCursor = new Vector2(boundsMax.X + gap, center.Y);

        switch (p.Icon)
        {
            case IconPlace.Center:
            {
                var size = Math.Min(iconSize, Math.Max((ringR - ringW * 0.5f) * 2f - 4f * s, 8f));
                DrawIcon(center, size, true);
                boundsMin = Vector2.Min(boundsMin, center - new Vector2(size * 0.5f));
                boundsMax = Vector2.Max(boundsMax, center + new Vector2(size * 0.5f));
                break;
            }
            case IconPlace.Start:
            {
                var c = new Vector2(boundsMin.X - gap - iconSize * 0.5f, center.Y);
                DrawIcon(c, iconSize, true);
                boundsMin.X = c.X - iconSize * 0.5f;
                break;
            }
            case IconPlace.End:
            {
                var c = new Vector2(boundsMax.X + gap + iconSize * 0.5f, center.Y);
                DrawIcon(c, iconSize, true);
                boundsMax.X = c.X + iconSize * 0.5f;
                endCursor.X = boundsMax.X + gap;
                break;
            }
        }

        DrawQueued(endCursor, new Vector2(1, 0), ref boundsMin, ref boundsMax);
        DrawTexts(boundsMin, boundsMax, horizontalBar: false, ref boundsMin, ref boundsMax);

        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }

    /// <summary>Angle, in ImGui's convention, of a fraction of the way round the ring.</summary>
    private float RingAngle(float u)
    {
        var deg = p.RingStart + (p.CounterClockwise ? -1f : 1f) * Math.Clamp(u, 0f, 1f) * p.RingSweep;
        return deg * MathF.PI / 180f - MathF.PI * 0.5f;
    }

    private Vector2 RingPoint(float u, float radius)
    {
        var a = RingAngle(u);
        return center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
    }

    private void Arc(float u0, float u1, float radius, float thickness, uint color, bool closed)
    {
        if (radius <= 0f) return;
        if (closed)
        {
            dl.AddCircle(center, radius, color, 96, thickness);
            return;
        }

        var a0 = RingAngle(u0);
        var a1 = RingAngle(u1);
        var span = MathF.Abs(a1 - a0);
        if (span < 0.002f) return;

        var segments = Math.Max(4, (int)MathF.Ceiling(span / Tau * 96f));
        dl.PathArcTo(center, radius, a0, a1, segments);
        dl.PathStroke(color, ImDrawFlags.None, thickness);
    }

    /// <summary>A short radial line across the band at a fraction of the way round.</summary>
    private void Tick(float u, float halfLength, uint color, float thickness)
    {
        var a = RingAngle(u);
        var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
        dl.AddLine(center + dir * (ringR - halfLength), center + dir * (ringR + halfLength), color, Math.Max(thickness, 1f));
    }

    // ------------------------------------------------------------------
    // Icons
    // ------------------------------------------------------------------

    private void DrawIcon(Vector2 at, float size, bool round)
    {
        if (f.Icon == 0 || size < 4f || IsGcd) return;

        var half = new Vector2(size * 0.5f);
        var min = at - half;
        var max = at + half;
        var rounding = round ? size * 0.5f : Math.Min(4f * s, size * 0.2f);

        if (Plugin.TextureProvider.TryGetFromGameIcon(new GameIconLookup(f.Icon), out var texture))
        {
            var wrap = texture.GetWrapOrEmpty();
            var tint = f.Phase switch
            {
                CastPhase.Interrupted => new Vector4(0.55f, 0.5f, 0.5f, 1f),
                CastPhase.Gcd => new Vector4(0.85f, 0.85f, 0.85f, 0.85f),
                _ => Vector4.One,
            };
            // Game icons have a soft border baked in; trim it so rounded corners look clean.
            var inset = new Vector2(0.04f);
            dl.AddImageRounded(wrap.Handle, min, max, inset, Vector2.One - inset, Col(tint), rounding);
        }

        if (p.FrameThickness > 0.01f || round)
            dl.AddRect(min, max, Col(frameColor, 0.9f), rounding, ImDrawFlags.None, Math.Max(p.FrameThickness * s, 1f));
    }

    /// <summary>The icon of the action already queued, popping in just past the end of the castbar.</summary>
    private void DrawQueued(Vector2 cursor, Vector2 direction, ref Vector2 boundsMin, ref Vector2 boundsMax)
    {
        if (!p.ShowQueuedIcon || f.QueuedIcon == 0) return;
        if (!Plugin.TextureProvider.TryGetFromGameIcon(new GameIconLookup(f.QueuedIcon), out var texture)) return;

        var size = Math.Max(p.IconSize * 0.62f, 16f) * s;
        var at = cursor + direction * (size * 0.5f + 2f * s);
        var min = at - new Vector2(size * 0.5f);
        var max = at + new Vector2(size * 0.5f);
        var rounding = Math.Min(4f * s, size * 0.25f);

        dl.AddRectFilled(min - new Vector2(3f * s), max + new Vector2(3f * s), Col(p.Colors.Queue, 0.25f), rounding + 3f * s);
        dl.AddImageRounded(texture.GetWrapOrEmpty().Handle, min, max, new Vector2(0.04f), new Vector2(0.96f), Col(Vector4.One, 0.92f), rounding);
        dl.AddRect(min, max, Col(p.Colors.Queue), rounding, ImDrawFlags.None, Math.Max(1.5f * s, 1f));

        // A small chevron pointing at it from the bar.
        var tip = at - direction * (size * 0.5f + 1f * s);
        var side = new Vector2(-direction.Y, direction.X) * 3.5f * s;
        dl.AddTriangleFilled(tip, tip - direction * 4f * s + side, tip - direction * 4f * s - side, Col(p.Colors.Queue));

        boundsMin = Vector2.Min(boundsMin, min);
        boundsMax = Vector2.Max(boundsMax, max);
    }

    // ------------------------------------------------------------------
    // Text
    // ------------------------------------------------------------------

    /// <summary>
    /// What the name line says right now. The name gives way to the cue that matters at that
    /// moment: a warning while you move too early, "Move" once it's safe, and after a cancelled
    /// cast, how early you moved.
    /// </summary>
    private (string Text, Vector4 Color, bool Cue) Headline()
    {
        var c = p.Colors;
        switch (f.Phase)
        {
            case CastPhase.Interrupted:
                if (ctx.Coach && !float.IsNaN(f.MovedEarlyBy))
                {
                    return f.MovedEarlyBy > 0.005f
                        ? ($"Moved {f.MovedEarlyBy.ToString("0.00", CultureInfo.InvariantCulture)}s too early", c.Danger, true)
                        : ("Cancelled inside the window", c.Danger, true);
                }
                return ("Interrupted", c.Danger, true);
            case CastPhase.Casting when ctx.MoveWarning && f.Danger:
                return ("Don't move!", c.Danger, true);
            case CastPhase.Casting when p.ShowMoveCue && f.Safe && f.SlideAt < f.Total:
                return ("Move  ›", c.Safe, true);
            case CastPhase.Completed when p.ShowMoveCue && f.Moving:
                return ("Move  ›", c.Safe, true);
            case CastPhase.Gcd:
                return ("GCD", c.Gcd, false);
            default:
                return (f.Name ?? string.Empty, c.Text, false);
        }
    }

    private string TimeLabel()
    {
        if (f.Phase is CastPhase.Completed or CastPhase.Interrupted) return string.Empty;

        var format = p.Decimals switch { 0 => "0", 2 => "0.00", _ => "0.0" };
        string N(float v) => v.ToString(format, CultureInfo.InvariantCulture);

        if (f.Phase == CastPhase.Gcd) return N(f.Remaining);

        return p.Time switch
        {
            TimeFormat.Elapsed => N(f.Elapsed),
            TimeFormat.ElapsedOfTotal => $"{N(f.Elapsed)} / {N(f.Total)}",
            _ => N(f.Remaining),
        };
    }

    private void DrawTexts(Vector2 shapeMin, Vector2 shapeMax, bool horizontalBar, ref Vector2 boundsMin, ref Vector2 boundsMax)
    {
        var (headline, headColor, cue) = Headline();
        var time = TimeLabel();

        var namePlace = p.NameText;
        var timePlace = p.TimeText;

        // A cue has to show somewhere: borrow the time's slot when the name is hidden.
        if (namePlace == TextPlace.Hidden && cue && timePlace != TextPlace.Hidden)
        {
            namePlace = timePlace;
            time = string.Empty;
        }

        if (namePlace == TextPlace.Hidden) headline = string.Empty;
        if (timePlace == TextPlace.Hidden) time = string.Empty;
        if (headline.Length == 0 && time.Length == 0) return;

        var nameFont = fonts.Get(p.NameFont);
        var timeFont = fonts.Get(p.TimeFont);
        var nameSize = FontService.BaseSize(p.NameFont) * p.TextScale * s;
        var timeSize = FontService.BaseSize(p.TimeFont) * p.TextScale * s;
        var gap = 3f * s;
        var timeColor = f.Phase == CastPhase.Casting && f.Safe && p.ShowMoveCue ? p.Colors.Safe : p.Colors.Text;
        if (cue && !ctx.ReducedMotion && f.Phase == CastPhase.Casting && f.Danger)
            headColor = Vector4.Lerp(headColor, Vector4.One, 0.35f + 0.35f * MathF.Sin(ctx.Now * 22f));

        // Inside a horizontal bar: name on the left, time on the right, both shrunk to fit.
        if (horizontalBar)
        {
            var thick = shapeMax.Y - shapeMin.Y;
            var innerName = Math.Min(nameSize, thick * 0.95f);
            var innerTime = Math.Min(timeSize, thick * 1.1f);
            var pad = 6f * s;

            if (namePlace == TextPlace.Inside && headline.Length > 0 && innerName >= 7f)
            {
                var y = (shapeMin.Y + shapeMax.Y) * 0.5f - innerName * 0.5f;
                var x = p.Direction == FillDirection.RightToLeft ? shapeMax.X - pad - Measure(nameFont, innerName, headline).X : shapeMin.X + pad;
                Text(nameFont, innerName, new Vector2(x, y), headColor, headline);
                headline = string.Empty;
            }

            if (timePlace == TextPlace.Inside && time.Length > 0 && innerTime >= 7f)
            {
                var size = Measure(timeFont, innerTime, time);
                var y = (shapeMin.Y + shapeMax.Y) * 0.5f - size.Y * 0.5f;
                var x = p.Direction == FillDirection.RightToLeft ? shapeMin.X + pad : shapeMax.X - pad - size.X;
                Text(timeFont, innerTime, new Vector2(x, y), timeColor, time);
                time = string.Empty;
            }

            // Above or below a horizontal bar: name to the left, time to the right, sharing a line.
            foreach (var place in new[] { TextPlace.Above, TextPlace.Below })
            {
                var nameHere = namePlace == place && headline.Length > 0;
                var timeHere = timePlace == place && time.Length > 0;
                if (!nameHere && !timeHere) continue;

                var lineH = Math.Max(nameHere ? nameSize : 0f, timeHere ? timeSize : 0f);
                var y = place == TextPlace.Above ? shapeMin.Y - gap - lineH : shapeMax.Y + gap;
                if (nameHere)
                    Text(nameFont, nameSize, new Vector2(shapeMin.X + 1f * s, y + (lineH - nameSize)), headColor, headline);
                if (timeHere)
                {
                    var size = Measure(timeFont, timeSize, time);
                    Text(timeFont, timeSize, new Vector2(shapeMax.X - size.X - 1f * s, y + (lineH - timeSize)), timeColor, time);
                }

                if (place == TextPlace.Above) boundsMin.Y = Math.Min(boundsMin.Y, y);
                else boundsMax.Y = Math.Max(boundsMax.Y, y + lineH);
            }

            if (namePlace == TextPlace.Center && headline.Length > 0)
                Centered(nameFont, nameSize, center, headColor, headline);
            if (timePlace == TextPlace.Center && time.Length > 0)
                Centered(timeFont, timeSize, center, timeColor, time);
            return;
        }

        // Rings and vertical bars: centred lines, stacked above, below or in the middle.
        var bMin = boundsMin;
        var bMax = boundsMax;
        var aboveY = shapeMin.Y - gap;
        var belowY = shapeMax.Y + gap;
        var cx = (shapeMin.X + shapeMax.X) * 0.5f;
        if (p.Shape == CastShape.Ring) cx = center.X;

        void Place(TextPlace place, ImFontPtr font, float size, Vector4 color, string text)
        {
            if (text.Length == 0) return;
            var m = Measure(font, size, text);
            switch (place)
            {
                case TextPlace.Above:
                    aboveY -= m.Y;
                    Text(font, size, new Vector2(cx - m.X * 0.5f, aboveY), color, text);
                    bMin.Y = Math.Min(bMin.Y, aboveY);
                    aboveY -= gap * 0.5f;
                    break;
                case TextPlace.Below:
                    Text(font, size, new Vector2(cx - m.X * 0.5f, belowY), color, text);
                    belowY += m.Y + gap * 0.5f;
                    bMax.Y = Math.Max(bMax.Y, belowY);
                    break;
                case TextPlace.Inside:
                case TextPlace.Center:
                    Centered(font, size, center, color, text);
                    break;
            }

            bMin.X = Math.Min(bMin.X, cx - m.X * 0.5f);
            bMax.X = Math.Max(bMax.X, cx + m.X * 0.5f);
        }

        // Time sits closest to the shape, the headline beyond it, whichever side they're on.
        if (namePlace == TextPlace.Above && timePlace == TextPlace.Above)
        {
            Place(TextPlace.Above, timeFont, timeSize, timeColor, time);
            Place(TextPlace.Above, nameFont, nameSize, headColor, headline);
        }
        else if (namePlace == TextPlace.Below && timePlace == TextPlace.Below)
        {
            Place(TextPlace.Below, timeFont, timeSize, timeColor, time);
            Place(TextPlace.Below, nameFont, nameSize, headColor, headline);
        }
        else
        {
            Place(namePlace, nameFont, nameSize, headColor, headline);
            Place(timePlace, timeFont, timeSize, timeColor, time);
        }

        boundsMin = bMin;
        boundsMax = bMax;
    }

    private static Vector2 Measure(ImFontPtr font, float size, string text) => TextMetrics.Measure(font, size, text);

    private void Centered(ImFontPtr font, float size, Vector2 at, Vector4 color, string text)
    {
        var m = Measure(font, size, text);
        Text(font, size, at - m * 0.5f, color, text);
    }

    /// <summary>Text with a soft dark outline, so it reads over any scenery.</summary>
    private void Text(ImFontPtr font, float size, Vector2 pos, Vector4 color, string text)
    {
        pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
        var shadow = Col(new Vector4(0f, 0f, 0f, 0.85f));
        var o = Math.Max(1f, size / 18f);
        dl.AddText(font, size, pos + new Vector2(o, o), shadow, text);
        dl.AddText(font, size, pos + new Vector2(-o * 0.6f, 0f), shadow, text);
        dl.AddText(font, size, pos + new Vector2(0f, -o * 0.6f), shadow, text);
        dl.AddText(font, size, pos, Col(color), text);
    }

    // ------------------------------------------------------------------
    // Easing
    // ------------------------------------------------------------------

    private static float SmoothStep(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
}
