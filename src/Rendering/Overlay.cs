using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Chant.Models;
using Chant.Tracking;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures;

namespace Chant.Rendering;

/// <summary>
/// Puts the castbar on screen: picks the frame to show (a real cast, or the test loop), works out
/// where it goes, fades it, hides the game's own cast bar while Chant stands in for it, and lets
/// you drag it into place while the settings are open.
/// </summary>
public sealed class Overlay
{
    private readonly Plugin plugin;
    private readonly CastRenderer renderer;

    private float appear;
    private Vector2 lastAnchor;
    private bool hasAnchor;

    /// <summary>The frame drawn last, for the status pill and Diagnostics.</summary>
    public CastFrame Shown { get; private set; } = CastFrame.Empty;

    public Overlay(Plugin plugin, CastRenderer renderer)
    {
        this.plugin = plugin;
        this.renderer = renderer;
    }

    private Configuration Config => plugin.Configuration;

    /// <summary>Scale for resolution: presets are designed at 1080p.</summary>
    public static float ResolutionScale => Math.Max(ImGui.GetMainViewport().Size.Y / 1080f, 0.5f);

    /// <summary>Why real casts aren't being drawn right now, or null when they are.</summary>
    public string? SuppressedReason
    {
        get
        {
            if (!Config.Enabled) return "turned off";
            if (Plugin.HiddenReason is { } hidden) return hidden;
            if (Config.OnlyInCombat && !Plugin.Condition[ConditionFlag.InCombat]) return "out of combat";
            return null;
        }
    }

    public void Draw(double now, float dt)
    {
        var preset = Config.Active;
        var suppressed = SuppressedReason;
        var demo = plugin.TestMode || plugin.Positioning;

        var frame = plugin.Tracker.Frame;
        if (suppressed is not null || Config.HideUtilityCasts && frame.Kind == ActionKind.Utility && frame.Phase != CastPhase.Gcd)
            frame = CastFrame.Empty;

        // Test casts fill in whenever nothing real is going on.
        if (demo && !frame.Visible && Plugin.HiddenReason is null)
            frame = TestCasts.Evaluate(now, Config, plugin.Catalog, preset.Linger);

        // The game's own cast bar stays hidden whenever Chant would draw a real cast instead.
        var standIn = suppressed is null && Config.HideGameCastBar
                      && !(Config.HideUtilityCasts && plugin.Tracker.Frame.Kind == ActionKind.Utility);
        GameCastBar.SetHidden(standIn);

        appear = frame.Visible ? Math.Min(appear + dt * 14f, 1f) : 0f;
        Shown = frame;

        if (!TryAnchor(preset, out var anchor))
        {
            DrawRaises(null, now);
            return;
        }

        var context = new RenderContext
        {
            Anchor = anchor,
            Scale = ResolutionScale,
            Opacity = Config.Opacity * appear,
            Now = (float)now,
            Linger = preset.Linger,
            ReducedMotion = plugin.ReducedMotion,
            MoveWarning = Config.MoveWarning,
            Coach = Config.InterruptCoach,
        };

        var dl = ImGui.GetBackgroundDrawList();
        if (frame.Visible)
            renderer.Draw(dl, frame, preset, context);

        if (plugin.Positioning)
            DrawMover(preset, anchor);

        DrawRaises(frame.Visible ? renderer.BoundsMax.Y : anchor.Y + preset.Offset.Y * ResolutionScale + 30f * ResolutionScale, now);
    }

    /// <summary>Where the castbar is centred this frame, before the preset's own offset.</summary>
    private bool TryAnchor(Preset preset, out Vector2 anchor)
    {
        var viewport = ImGui.GetMainViewport();
        if (preset.Anchor == AnchorMode.Screen)
        {
            anchor = viewport.Pos + preset.ScreenPosition * viewport.Size;
            return true;
        }

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is not null && Plugin.GameGui.WorldToScreen(player.Position + new Vector3(0f, preset.CharacterHeight, 0f), out var screen))
        {
            // Smooth over the sub-pixel jitter of the camera following you.
            lastAnchor = hasAnchor && Vector2.DistanceSquared(screen, lastAnchor) < 2500f ? Vector2.Lerp(lastAnchor, screen, 0.6f) : screen;
            hasAnchor = true;
            anchor = lastAnchor;
            return true;
        }

        // Positioning works out of the game too, around the middle of the screen.
        if (plugin.Positioning)
        {
            anchor = viewport.Pos + viewport.Size * 0.5f;
            return true;
        }

        hasAnchor = false;
        anchor = default;
        return false;
    }

    // ------------------------------------------------------------------
    // Positioning
    // ------------------------------------------------------------------

    /// <summary>
    /// A drag handle over the castbar. Dragging moves the screen position, or the offset from your
    /// character for presets that follow you.
    /// </summary>
    private void DrawMover(Preset preset, Vector2 anchor)
    {
        var min = renderer.BoundsMin;
        var max = renderer.BoundsMax;
        if (!Shown.Visible || max.X <= min.X)
        {
            var center = anchor + preset.Offset * ResolutionScale;
            min = center - new Vector2(60f, 20f);
            max = center + new Vector2(60f, 20f);
        }

        Handle("##ChantMover", min, max, "Drag to move · right-click when done", delta =>
        {
            if (preset.Anchor == AnchorMode.Screen)
            {
                var size = ImGui.GetMainViewport().Size;
                preset.ScreenPosition = Vector2.Clamp(preset.ScreenPosition + delta / size, Vector2.Zero, Vector2.One);
            }
            else
            {
                preset.Offset += delta / ResolutionScale;
            }
        });
    }

    /// <summary>
    /// An invisible window over a rectangle that can be dragged, outlined with a dashed frame and a
    /// label. Right-clicking it ends positioning.
    /// </summary>
    private void Handle(string id, Vector2 min, Vector2 max, string label, Action<Vector2> drag)
    {
        var pad = new Vector2(10f);
        min -= pad;
        max += pad;

        var fg = ImGui.GetForegroundDrawList();
        var pulse = 0.55f + 0.45f * MathF.Sin((float)plugin.Now * 4f);
        fg.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.84f, 0.69f, 0.41f, 0.08f)), 6f);
        DashedRect(fg, min, max, ImGui.GetColorU32(new Vector4(0.95f, 0.84f, 0.59f, 0.55f + 0.4f * pulse)));
        var ts = ImGui.CalcTextSize(label);
        var lp = new Vector2((min.X + max.X - ts.X) * 0.5f, min.Y - ts.Y - 4f);
        fg.AddRectFilled(lp - new Vector2(6f, 2f), lp + ts + new Vector2(6f, 2f), ImGui.GetColorU32(new Vector4(0.04f, 0.06f, 0.1f, 0.85f)), 4f);
        fg.AddText(lp, ImGui.GetColorU32(new Vector4(0.95f, 0.84f, 0.59f, 1f)), label);

        ImGui.SetNextWindowPos(min);
        ImGui.SetNextWindowSize(max - min);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;
        if (ImGui.Begin(id, flags))
        {
            ImGui.InvisibleButton("##drag", max - min);
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
            {
                drag(ImGui.GetIO().MouseDelta);
                plugin.ConfigWindow.MarkChanged();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                plugin.Positioning = false;
        }
        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private static void DashedRect(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        const float dash = 7f;
        const float gap = 5f;
        void Line(Vector2 a, Vector2 b)
        {
            var len = Vector2.Distance(a, b);
            var dir = (b - a) / Math.Max(len, 0.001f);
            for (var d = 0f; d < len; d += dash + gap)
                dl.AddLine(a + dir * d, a + dir * Math.Min(d + dash, len), color, 1.5f);
        }

        Line(min, new Vector2(max.X, min.Y));
        Line(new Vector2(max.X, min.Y), max);
        Line(max, new Vector2(min.X, max.Y));
        Line(new Vector2(min.X, max.Y), min);
    }

    // ------------------------------------------------------------------
    // Raise watch
    // ------------------------------------------------------------------

    private static readonly List<RaiseCast> Sample = [];

    /// <summary>
    /// Party members' raises in progress, as small pills: under the castbar, or wherever you
    /// dragged them.
    /// </summary>
    private void DrawRaises(float? top, double now)
    {
        if (!Config.RaiseWatch) return;
        var demo = plugin.TestMode || plugin.Positioning;
        if (SuppressedReason is not null && !demo) return;

        IReadOnlyList<RaiseCast> raises = plugin.Raises.Raises;
        if (raises.Count == 0 && demo)
        {
            // While positioning the sample stays put, so there's always something to drag.
            var t = plugin.Positioning ? 2f : (float)(now % 10.0);
            Sample.Clear();
            if (t < 6f)
                Sample.Add(new RaiseCast("Alisaie Leveilleur", "Alphinaud Leveilleur",
                    plugin.Catalog.Get(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Action, 7523).Icon, 6f - t, t / 6f, false));
            raises = Sample;
        }

        if (raises.Count == 0) return;

        var viewport = ImGui.GetMainViewport();
        var scale = ResolutionScale;
        float y, cx;
        if (Config.RaiseWatchPlacement == RaiseWatchPlacement.Free)
        {
            cx = viewport.Pos.X + Config.RaiseWatchPosition.X * viewport.Size.X;
            y = viewport.Pos.Y + Config.RaiseWatchPosition.Y * viewport.Size.Y;
        }
        else
        {
            y = top.HasValue ? top.Value + 10f * scale : viewport.Pos.Y + viewport.Size.Y * 0.78f;
            cx = Config.Active.Anchor == AnchorMode.Screen
                ? viewport.Pos.X + Config.Active.ScreenPosition.X * viewport.Size.X + Config.Active.Offset.X * scale
                : (renderer.BoundsMin.X + renderer.BoundsMax.X) * 0.5f;
            if (!top.HasValue || float.IsNaN(cx) || cx <= 0f) cx = viewport.Pos.X + viewport.Size.X * 0.5f;
        }

        var startY = y;
        var minX = float.MaxValue;
        var maxX = float.MinValue;

        var dl = ImGui.GetBackgroundDrawList();
        var palette = Config.Active.Colors;
        var font = ImGui.GetFont();
        var size = 15f * scale;
        var icon = 20f * scale;
        var alpha = Config.Opacity;

        foreach (var raise in raises)
        {
            var text = $"{raise.Caster}  →  {raise.Target}";
            var time = raise.Remaining.ToString("0.0", CultureInfo.InvariantCulture);
            var tw = TextMetrics.Measure(font, size, text).X;
            var timeW = TextMetrics.Measure(font, size, time).X;
            var w = icon + 8f * scale + tw + 12f * scale + timeW + 16f * scale;
            var h = icon + 8f * scale;
            var min = new Vector2(cx - w * 0.5f, y);
            var max = min + new Vector2(w, h);
            var accent = raise.OnYourTarget ? palette.Ready : palette.Raise;

            dl.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.03f, 0.04f, 0.07f, 0.82f * alpha)), h * 0.5f);
            // Progress of their cast along the bottom of the pill.
            dl.AddRectFilled(new Vector2(min.X + h * 0.5f, max.Y - 2.5f * scale),
                new Vector2(min.X + h * 0.5f + (w - h) * Math.Clamp(raise.Progress, 0f, 1f), max.Y - 1f * scale),
                ImGui.GetColorU32(accent with { W = 0.9f * alpha }));
            dl.AddRect(min, max, ImGui.GetColorU32(accent with { W = 0.75f * alpha }), h * 0.5f, ImDrawFlags.None, 1.2f * scale);

            var ip = new Vector2(min.X + 6f * scale, min.Y + 4f * scale);
            if (raise.Icon != 0 && Plugin.TextureProvider.TryGetFromGameIcon(new GameIconLookup(raise.Icon), out var tex))
                dl.AddImageRounded(tex.GetWrapOrEmpty().Handle, ip, ip + new Vector2(icon), Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)), icon * 0.5f);

            var textPos = new Vector2(ip.X + icon + 8f * scale, min.Y + (h - size) * 0.5f);
            dl.AddText(font, size, textPos, ImGui.GetColorU32(palette.Text with { W = alpha }), text);
            dl.AddText(font, size, new Vector2(max.X - 10f * scale - timeW, textPos.Y), ImGui.GetColorU32(accent with { W = alpha }), time);

            minX = Math.Min(minX, min.X);
            maxX = Math.Max(maxX, max.X);
            y = max.Y + 4f * scale;
        }

        if (plugin.Positioning)
        {
            var anchorX = cx;
            var anchorY = startY;
            Handle("##ChantRaiseMover", new Vector2(minX, startY), new Vector2(maxX, y), "Raise watch · drag to move", delta =>
            {
                // The first drag frees it from the castbar, starting from where it is now.
                if (Config.RaiseWatchPlacement != RaiseWatchPlacement.Free)
                {
                    Config.RaiseWatchPlacement = RaiseWatchPlacement.Free;
                    Config.RaiseWatchPosition = (new Vector2(anchorX, anchorY) - viewport.Pos) / viewport.Size;
                }

                Config.RaiseWatchPosition = Vector2.Clamp(Config.RaiseWatchPosition + delta / viewport.Size, Vector2.Zero, Vector2.One);
            });
        }
    }
}
