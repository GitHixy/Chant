using System;
using System.Numerics;
using Chant.Models;
using Chant.Rendering;
using Chant.Tracking;
using Dalamud.Bindings.ImGui;

namespace Chant.UI;

/// <summary>
/// A small stage inside the settings window that plays the sample casts through the same renderer
/// the world uses, so what you see here is what you'll get in game. Presets that follow your
/// character are shown next to a stand-in for one.
/// </summary>
public sealed class PreviewCanvas
{
    /// <summary>Pixels per yalm on the stage, for character-anchored presets.</summary>
    private const float YalmPx = 46f;

    private readonly Plugin plugin;
    private readonly CastRenderer renderer;
    private float fit = 1f;

    public PreviewCanvas(Plugin plugin)
    {
        this.plugin = plugin;
        renderer = new CastRenderer(plugin.Fonts);
    }

    /// <summary>The frame shown last, for the caption under the stage.</summary>
    public CastFrame Frame { get; private set; } = CastFrame.Empty;

    public void Draw(string id, Vector2 size, Preset preset)
    {
        var config = plugin.Configuration;
        var width = size.X > 0 ? size.X : ImGui.GetContentRegionAvail().X;
        var p = ImGui.GetCursorScreenPos();
        var canvas = new Vector2(width, size.Y);
        var max = p + canvas;

        ImGui.InvisibleButton(id, canvas);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, max, Theme.U32(new Vector4(0.03f, 0.05f, 0.08f, 1f)), Theme.Radius);
        dl.AddRectFilledMultiColor(p, new Vector2(max.X, p.Y + size.Y * 0.6f),
            Theme.U32(Theme.Crystal, 0.07f), Theme.U32(Theme.Gold, 0.05f), Theme.U32(Theme.Crystal, 0f), Theme.U32(Theme.Crystal, 0f));
        dl.AddRect(p, max, Theme.U32(Theme.Line), Theme.Radius, ImDrawFlags.None, 1f);
        dl.PushClipRect(p, max, true);

        var stageCenter = p + canvas * new Vector2(0.5f, 0.52f);
        var anchor = stageCenter;

        if (preset.Anchor == AnchorMode.Character)
        {
            // A stand-in for your character: feet on a soft shadow, the castbar at the chosen height.
            var feet = p + canvas * new Vector2(0.5f, 0.86f);
            DrawStandIn(dl, feet);
            anchor = feet - new Vector2(0f, preset.CharacterHeight * YalmPx);
        }

        var frame = TestCasts.Evaluate(plugin.Now, config, plugin.Catalog, preset.Linger);
        Frame = frame;

        var context = new RenderContext
        {
            Anchor = anchor,
            Scale = fit,
            Opacity = config.Opacity,
            Now = (float)plugin.Now,
            Linger = preset.Linger,
            ReducedMotion = plugin.ReducedMotion,
            MoveWarning = config.MoveWarning,
            Coach = config.InterruptCoach,
        };

        if (preset.Anchor == AnchorMode.Screen)
        {
            // Screen presets are shown centred; their offset still applies.
            context.Anchor = stageCenter;
        }

        renderer.Draw(dl, frame, preset, context);

        // Shrink to fit wide or tall presets; grow back slowly when there's room again.
        if (frame.Visible)
        {
            var bw = renderer.BoundsMax.X - renderer.BoundsMin.X;
            var bh = renderer.BoundsMax.Y - renderer.BoundsMin.Y;
            var room = Math.Min((canvas.X - 40f) / Math.Max(bw, 1f), (canvas.Y - 30f) / Math.Max(bh, 1f));
            var want = Math.Clamp(fit * room, 0.3f, 1f);
            fit = want < fit ? want : fit + (want - fit) * 0.05f;
        }

        dl.PopClipRect();

        // Caption: what the stand-in is doing right now.
        var (caption, color) = Caption(frame);
        if (caption.Length > 0)
        {
            var ts = ImGui.CalcTextSize(caption);
            var cp = new Vector2(p.X + 10f, max.Y - ts.Y - 8f);
            dl.AddRectFilled(cp - new Vector2(6f, 3f), cp + ts + new Vector2(6f, 3f), Theme.U32(Theme.Ink, 0.85f), 4f);
            dl.AddText(cp, Theme.U32(color), caption);
        }
    }

    private static (string Text, Vector4 Color) Caption(CastFrame f) => f.Phase switch
    {
        CastPhase.Casting when f.Danger => ("Moving too early: the cast is about to be cancelled", Theme.Bad),
        CastPhase.Casting when f.Safe && f.Moving => ("Slidecasting: moving inside the window", Theme.Good),
        CastPhase.Casting when f.Safe => ("Safe to move", Theme.Good),
        CastPhase.Casting when f.Ready => ("Get ready to move", Theme.Warn),
        CastPhase.Casting => ("Hold still", Theme.TextMuted),
        CastPhase.Gcd => ("Global cooldown after an instant action", Theme.Crystal),
        CastPhase.Completed => ("Cast went off", Theme.Good),
        CastPhase.Interrupted => ("Cancelled by moving too early", Theme.Bad),
        _ => (string.Empty, Theme.TextMuted),
    };

    private static void DrawStandIn(ImDrawListPtr dl, Vector2 feet)
    {
        var body = Theme.U32(new Vector4(0.55f, 0.62f, 0.74f, 0.55f));
        dl.AddCircleFilled(feet, 22f, Theme.U32(new Vector4(0f, 0f, 0f, 0.35f)), 32);
        dl.AddRectFilled(feet + new Vector2(-11f, -62f), feet + new Vector2(11f, -6f), body, 9f);
        dl.AddCircleFilled(feet + new Vector2(0f, -76f), 11f, body, 24);
    }
}
