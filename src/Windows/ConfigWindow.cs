using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Chant.Models;
using Chant.Presets;
using Chant.Rendering;
using Chant.Services;
using Chant.Tracking;
using Chant.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace Chant.Windows;

public class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private readonly PreviewCanvas preview;

    private int section;

    private const int ReportLineLimit = 200;
    private const float LabelWidth = 170f;
    private const float ControlWidth = 230f;
    private const float PreviewHeight = 210f;

    private bool changed;
    private double saveAt = double.MaxValue;

    // Presets view state
    private string saveName = string.Empty;
    private string importMessage = string.Empty;
    private Vector4 importColor = Theme.TextMuted;
    private string? confirmDeleteId;

    // Diagnostics view state. The filtered list is rebuilt only when the log or the filter changes.
    private int logLevelFilter;
    private string logSearch = string.Empty;
    private bool logAutoScroll = true;
    private readonly HashSet<LogEntry> selectedLogLines = [];
    private List<LogEntry> visibleLogLines = [];
    private (int Revision, int Level, string Search) logViewKey = (-1, -1, string.Empty);
    private int scrolledRevision = -1;

    private static readonly (FontAwesomeIcon Icon, string Label, string Blurb)[] Sections =
    [
        (FontAwesomeIcon.LayerGroup, "Presets", "Pick a look, save your own, share it as a code"),
        (FontAwesomeIcon.DrawPolygon, "Shape", "Bar or ring, size, and where it sits"),
        (FontAwesomeIcon.Palette, "Colours", "Spell kinds, the slidecast states and the frame"),
        (FontAwesomeIcon.Font, "Text & icon", "Spell icon, name, timer and motion"),
        (FontAwesomeIcon.Stopwatch, "Timing", "Slidecast and queue windows, and how you're doing"),
        (FontAwesomeIcon.SlidersH, "General", "Master switch, the game's cast bar, extras"),
        (FontAwesomeIcon.Bug, "Diagnostics", "What Chant sees, log and bug reports"),
        (FontAwesomeIcon.InfoCircle, "About", "Version, links and support"),
    ];

    public ConfigWindow(Plugin plugin)
        : base("Chant Settings###ChantConfig")
    {
        this.plugin = plugin;
        preview = new PreviewCanvas(plugin);

        Size = new Vector2(860, 680);
        SizeConstraintMin = new Vector2(720, 520);
        SizeConstraintMax = new Vector2(1400, 1300);
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    }

    private ConfigurationService ConfigService => plugin.ConfigService;
    private Configuration Config => plugin.Configuration;
    private Preset P => Config.Active;

    /// <summary>Something outside this window (dragging the castbar) changed a setting.</summary>
    public void MarkChanged() => saveAt = plugin.Now + 1.0;

    /// <summary>Sliders fire every frame while dragged; save once things settle.</summary>
    public void SaveIfSettled(double now)
    {
        if (now < saveAt) return;
        saveAt = double.MaxValue;
        ConfigService.Save();
    }

    protected override void DrawContents()
    {
        try
        {
            BgAlpha = 0.94f;
            changed = false;

            Theme.WindowHeader(FontAwesomeIcon.Magic, "Chant", Sections[section].Blurb, DrawHeaderActions);

            var avail = ImGui.GetContentRegionAvail();
            const float railWidth = 158f;

            DrawNavRail(new Vector2(railWidth, avail.Y));

            ImGui.SameLine(0, 12);

            using (var pane = Theme.Region("##ConfigPane", new Vector2(0, avail.Y)))
            {
                if (pane)
                {
                    switch (section)
                    {
                        case 0: DrawPresets(); break;
                        case 1: DrawShape(); break;
                        case 2: DrawColours(); break;
                        case 3: DrawTextAndIcon(); break;
                        case 4: DrawTiming(); break;
                        case 5: DrawGeneral(); break;
                        case 6: DrawDiagnostics(); break;
                        case 7: DrawAbout(); break;
                    }

                    ImGui.Dummy(new Vector2(0, 8));
                }
            }

            if (changed)
                MarkChanged();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Error drawing config window");
            ImGui.TextColored(Theme.Bad, "Error drawing settings.");
        }
    }

    protected override void OnClosed()
    {
        if (saveAt != double.MaxValue)
        {
            saveAt = double.MaxValue;
            ConfigService.Save();
        }
    }

    private void DrawHeaderActions()
    {
        var width = ImGui.GetContentRegionAvail().X;
        const float testWidth = 130f;
        const float moveWidth = 140f;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + width - testWidth - moveWidth - 8f);

        var testing = plugin.TestMode;
        if (testing ? Theme.PrimaryButton("Stop test", new Vector2(testWidth, 0)) : Theme.GhostButton("Test casts", new Vector2(testWidth, 0), Theme.Gold))
            plugin.TestMode = !plugin.TestMode;
        if (ImGui.IsItemHovered())
            Theme.Tooltip("Plays sample casts on screen: a slidecast, a cast held still, a raise cancelled by moving too early, "
                          + "an instant with its global cooldown and a mount. Also /chant test.");

        ImGui.SameLine(0, 8);
        var moving = plugin.Positioning;
        if (moving ? Theme.PrimaryButton("Done moving", new Vector2(moveWidth, 0)) : Theme.GhostButton("Move castbar", new Vector2(moveWidth, 0), Theme.Crystal))
            plugin.Positioning = !plugin.Positioning;
        if (ImGui.IsItemHovered())
            Theme.Tooltip("Shows a handle over the castbar so you can drag it where you want it. "
                          + "Presets that follow your character move relative to it. Right-click the handle when done. Also /chant move.");
    }

    private void DrawNavRail(Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        dl.AddRectFilled(origin, origin + size, Theme.U32(Theme.Panel, 0.55f), Theme.Radius);
        dl.AddRect(origin, origin + size, Theme.U32(Theme.Line, 0.8f), Theme.Radius, ImDrawFlags.None, 1f);

        using var child = Theme.Region("##ConfigNav", size);
        if (!child) return;

        ImGui.Dummy(new Vector2(0, 4));

        for (var i = 0; i < Sections.Length; i++)
        {
            var (icon, label, _) = Sections[i];
            var selected = section == i;

            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X - 8;
            const float h = 34f;

            ImGui.SetCursorScreenPos(new Vector2(p.X + 4, p.Y));
            if (ImGui.InvisibleButton($"##nav{i}", new Vector2(w, h)))
                section = i;

            var hovered = ImGui.IsItemHovered();
            var min = new Vector2(p.X + 4, p.Y);
            var max = new Vector2(p.X + 4 + w, p.Y + h);

            if (selected)
            {
                dl.AddRectFilled(min, max, Theme.U32(Theme.Gold, 0.13f), Theme.Radius);
                dl.AddRectFilled(min, new Vector2(min.X + 2.5f, max.Y), Theme.U32(Theme.Gold, 0.95f), 1.5f);
            }
            else if (hovered)
            {
                dl.AddRectFilled(min, max, Theme.U32(Theme.Crystal, 0.12f), Theme.Radius);
            }

            var tint = selected ? Theme.GoldBright : hovered ? Theme.Text : Theme.TextMuted;

            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var glyph = icon.ToIconString();
                var gs = ImGui.CalcTextSize(glyph);
                dl.AddText(new Vector2(min.X + 15 - gs.X * 0.5f, min.Y + (h - gs.Y) * 0.5f), Theme.U32(tint), glyph);
            }

            var ts = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(min.X + 30, min.Y + (h - ts.Y) * 0.5f), Theme.U32(tint), label);

            ImGui.Dummy(new Vector2(0, 2));
        }

        // Status pill at the bottom of the rail: is the castbar being drawn right now?
        var (statusText, statusColor) = StatusSummary();
        var pillY = origin.Y + size.Y - 34f;
        var pillMin = new Vector2(origin.X + 8, pillY);
        var pillMax = new Vector2(origin.X + size.X - 8, pillY + 24f);
        dl.AddRectFilled(pillMin, pillMax, Theme.U32(statusColor, 0.12f), 12f);
        dl.AddCircleFilled(new Vector2(pillMin.X + 12, pillMin.Y + 12), 4f, Theme.U32(statusColor), 12);
        dl.AddText(new Vector2(pillMin.X + 22, pillMin.Y + (24f - ImGui.GetTextLineHeight()) * 0.5f), Theme.U32(statusColor), statusText);
    }

    private (string Text, Vector4 Color) StatusSummary()
    {
        if (!Config.Enabled) return ("Turned off", Theme.Warn);
        if (Plugin.HiddenReason is "PvP") return ("Off in PvP", Theme.TextMuted);
        if (plugin.Overlay.SuppressedReason is not null) return ("Hidden", Theme.TextMuted);
        if (plugin.TestMode) return ("Test casts", Theme.Crystal);
        var shown = plugin.Overlay.Shown;
        if (shown.Phase == CastPhase.Casting) return shown.Safe ? ("Safe to move", Theme.Good) : ("Casting", Theme.Gold);
        return ("Active", Theme.Good);
    }

    // ------------------------------------------------------------------
    // Presets
    // ------------------------------------------------------------------

    private void DrawPreview()
    {
        preview.Draw("##Preview", new Vector2(0, PreviewHeight), P);
        ImGui.Dummy(new Vector2(0, 4));
    }

    private void DrawPresets()
    {
        DrawPreview();

        Theme.SectionHeader("Built-in presets", FontAwesomeIcon.LayerGroup);
        DrawPresetTiles(BuiltInPresets.All, "builtin");

        ImGui.Dummy(new Vector2(0, 6));
        Wrapped("Change anything you like in the other pages, and drag the castbar where you want it. Every preset "
                + "remembers your changes and its position, so you can switch back and forth freely. "
                + "Reset in Text & icon brings a preset back to how it shipped.");

        ImGui.Dummy(new Vector2(0, 10));
        Theme.SectionHeader("My presets", FontAwesomeIcon.Star);

        Row("Save current as");
        ImGui.SetNextItemWidth(ControlWidth);
        ImGui.InputTextWithHint("##SaveName", string.IsNullOrEmpty(P.BasedOn) ? "My castbar" : $"My {P.BasedOn}", ref saveName, 40);
        ImGui.SameLine(0, 8);
        if (Theme.PrimaryButton("Save", new Vector2(80, 0)))
        {
            var copy = P.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = string.IsNullOrWhiteSpace(saveName) ? (string.IsNullOrEmpty(P.BasedOn) ? "My castbar" : $"My {P.BasedOn}") : saveName.Trim();
            copy.Author = string.IsNullOrEmpty(copy.Author) || copy.Author == "GitHixy" ? string.Empty : copy.Author;
            Config.UserPresets.RemoveAll(u => u.Name == copy.Name);
            Config.UserPresets.Add(copy);
            P.BasedOn = copy.Name;
            saveName = string.Empty;
            changed = true;
        }

        ImGui.Dummy(new Vector2(0, 4));
        if (Config.UserPresets.Count == 0)
            ImGui.TextColored(Theme.TextFaint, "Nothing saved yet. Presets you save or import show up here.");
        else
            DrawPresetTiles(Config.UserPresets, "user");

        ImGui.Dummy(new Vector2(0, 10));
        Theme.SectionHeader("Share", FontAwesomeIcon.ShareAlt);

        if (Theme.GhostButton("Copy my castbar as a code", new Vector2(220, 30), Theme.Gold))
        {
            ImGui.SetClipboardText(PresetCodec.Encode(P));
            importMessage = "Copied. Paste it anywhere: anyone with Chant can import it.";
            importColor = Theme.Good;
        }

        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("Import from clipboard", new Vector2(190, 30), Theme.Crystal))
        {
            if (PresetCodec.TryDecode(ImGui.GetClipboardText() ?? string.Empty, out var imported, out var error))
            {
                Config.UserPresets.Add(imported);
                Apply(imported);
                importMessage = $"Imported \"{imported.Name}\" and switched to it. It's saved under My presets.";
                importColor = Theme.Good;
            }
            else
            {
                importMessage = error;
                importColor = Theme.Bad;
            }
        }

        if (importMessage.Length > 0)
        {
            ImGui.Dummy(new Vector2(0, 2));
            Wrapped(importMessage, importColor);
        }
    }

    /// <summary>Every preset as a selectable tile: a miniature of its shape, its name and one line about it.</summary>
    private void DrawPresetTiles(IReadOnlyList<Preset> presets, string idPrefix)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var columns = avail > 600f ? 3 : 2;
        const float gap = 8f;
        const float h = 84f;
        var w = (avail - gap * (columns - 1)) / columns;
        var dl = ImGui.GetWindowDrawList();
        var user = idPrefix == "user";
        Preset? delete = null;

        for (var i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            if (i % columns != 0) ImGui.SameLine(0, gap);

            var p = ImGui.GetCursorScreenPos();
            var max = p + new Vector2(w, h);

            if (ImGui.InvisibleButton($"##{idPrefix}{i}", new Vector2(w, h)))
                Apply(preset);

            var hovered = ImGui.IsItemHovered();
            var selected = P.BasedOn == preset.Name;
            var edited = Config.PresetEdits.TryGetValue(preset.Name, out var mine);
            var shown = selected ? P : edited ? mine! : preset;
            var accent = shown.Colors.Damage;

            if (user)
            {
                using var popup = ImRaii.ContextPopupItem($"##ctx{idPrefix}{i}");
                if (popup.Success)
                {
                    if (ImGui.Selectable("Copy as a code"))
                        ImGui.SetClipboardText(PresetCodec.Encode(preset));
                    if (ImGui.Selectable("Overwrite with my castbar"))
                    {
                        var copy = P.Clone();
                        copy.Id = preset.Id;
                        copy.Name = preset.Name;
                        Config.UserPresets[i] = copy;
                        Config.PresetEdits.Remove(preset.Name);
                        changed = true;
                    }
                    if (ImGui.Selectable(confirmDeleteId == preset.Id ? "Click again to delete" : "Delete"))
                    {
                        if (confirmDeleteId == preset.Id) delete = preset;
                        else confirmDeleteId = preset.Id;
                    }
                }
            }

            dl.AddRectFilled(p, max, Theme.U32(selected ? Theme.Alpha(Theme.Gold, 0.10f) : hovered ? Theme.Alpha(Theme.Crystal, 0.08f) : Theme.Alpha(Theme.Surface, 0.6f)), Theme.Radius);
            dl.AddRect(p, max, Theme.U32(selected ? Theme.Gold : hovered ? Theme.Alpha(Theme.Crystal, 0.5f) : Theme.Line, selected ? 0.8f : 1f),
                Theme.Radius, ImDrawFlags.None, selected ? 1.5f : 1f);

            DrawMiniature(dl, new Vector2(p.X + 30, p.Y + 30), shown, accent);

            dl.AddText(new Vector2(p.X + 62, p.Y + 12), Theme.U32(selected ? Theme.GoldBright : Theme.Text), preset.Name);
            if (edited && !selected)
            {
                var nameWidth = ImGui.CalcTextSize(preset.Name).X;
                using (new Theme.FontScale(0.8f))
                    dl.AddText(new Vector2(p.X + 70 + nameWidth, p.Y + 14), Theme.U32(Theme.Crystal), "yours");
            }
            var sub = ShapeLabel(shown);
            using (new Theme.FontScale(0.86f))
            {
                dl.AddText(new Vector2(p.X + 62, p.Y + 31), Theme.U32(Theme.TextFaint), sub);
                var blurb = string.IsNullOrEmpty(preset.Description) ? (user ? "Right-click to share, overwrite or delete." : string.Empty) : preset.Description;
                dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(p.X + 10, p.Y + 54), Theme.U32(Theme.TextMuted),
                    Ellipsize(blurb, w - 20f), w - 20f);
            }

            if (hovered && !string.IsNullOrEmpty(preset.Description))
                Theme.Tooltip(preset.Description + (user ? "\n\nRight-click to share, overwrite or delete." : string.Empty));

            if (selected)
                dl.AddRectFilled(new Vector2(p.X + 10, max.Y - 2.5f), new Vector2(max.X - 10, max.Y - 1f), Theme.U32(Theme.Gold, 0.95f), 1f);

            if (i % columns == columns - 1 || i == presets.Count - 1)
                ImGui.Dummy(new Vector2(0, gap - ImGui.GetStyle().ItemSpacing.Y));
        }

        if (delete is not null)
        {
            Config.UserPresets.Remove(delete);
            Config.PresetEdits.Remove(delete.Name);
            confirmDeleteId = null;
            changed = true;
        }
    }

    /// <summary>One line of text cut to fit two lines of a tile, roughly.</summary>
    private static string Ellipsize(string text, float width)
    {
        var max = Math.Max((int)(width / 5.6f) * 2, 20);
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
    }

    private static string ShapeLabel(Preset p)
    {
        var shape = p.Shape == CastShape.Ring
            ? p.RingSweep >= 359.5f ? "Ring" : "Arc"
            : p.IsVertical ? "Vertical bar" : "Bar";
        var where = p.Anchor == AnchorMode.Character ? "follows you" : "on screen";
        return $"{shape} · {where}";
    }

    /// <summary>A tiny drawing of the preset's shape, two thirds full, in its own colours.</summary>
    private static void DrawMiniature(ImDrawListPtr dl, Vector2 c, Preset p, Vector4 accent)
    {
        var lockCol = Vector4.Lerp(accent, p.Colors.Lock, p.LockTint);
        var bg = Theme.U32(Theme.Ink, 0.9f);
        dl.AddCircleFilled(c, 22f, Theme.U32(Theme.Panel), 32);

        if (p.Shape == CastShape.Ring)
        {
            var sweep = p.RingSweep * MathF.PI / 180f;
            var start = p.RingStart * MathF.PI / 180f - MathF.PI * 0.5f;
            var dir = p.CounterClockwise ? -1f : 1f;
            var r = 13f;
            var t = Math.Clamp(p.RingThickness * 0.45f, 2f, 5f);
            dl.PathArcTo(c, r, start, start + dir * sweep, 32);
            dl.PathStroke(bg, ImDrawFlags.None, t + 1f);
            dl.PathArcTo(c, r, start, start + dir * sweep * 0.66f, 32);
            dl.PathStroke(Theme.U32(lockCol), ImDrawFlags.None, t);
            dl.PathArcTo(c, r, start + dir * sweep * 0.85f, start + dir * sweep, 12);
            dl.PathStroke(Theme.U32(p.Colors.Safe, 0.5f), ImDrawFlags.None, t);
            return;
        }

        var vertical = p.IsVertical;
        var len = 30f;
        var thick = Math.Clamp(p.Thickness * 0.4f, 2.5f, 9f);
        var size = vertical ? new Vector2(thick, len) : new Vector2(len, thick);
        var min = c - size * 0.5f;
        var max = c + size * 0.5f;
        dl.AddRectFilled(min, max, bg, Math.Min(p.Rounding * 0.4f, thick * 0.5f));
        if (vertical)
        {
            dl.AddRectFilled(new Vector2(min.X, max.Y - len * 0.66f), max, Theme.U32(lockCol), Math.Min(p.Rounding * 0.4f, thick * 0.5f));
            dl.AddRectFilled(min, new Vector2(max.X, min.Y + len * 0.15f), Theme.U32(p.Colors.Safe, 0.5f));
        }
        else
        {
            dl.AddRectFilled(min, new Vector2(min.X + len * 0.66f, max.Y), Theme.U32(lockCol), Math.Min(p.Rounding * 0.4f, thick * 0.5f));
            dl.AddRectFilled(new Vector2(max.X - len * 0.15f, min.Y), max, Theme.U32(p.Colors.Safe, 0.5f));
        }
    }

    /// <summary>
    /// Switches to a preset. The castbar you leave is kept as your version of its preset, and your
    /// version of the new one comes back if you've used it before.
    /// </summary>
    private void Apply(Preset source)
    {
        if (!string.IsNullOrEmpty(P.BasedOn))
            Config.PresetEdits[P.BasedOn] = P;

        Config.Active = Config.PresetEdits.TryGetValue(source.Name, out var mine) ? mine : BuiltInPresets.Instantiate(source);
        changed = true;
    }

    // ------------------------------------------------------------------
    // Shape
    // ------------------------------------------------------------------

    private void DrawShape()
    {
        DrawPreview();

        Theme.SectionHeader("Shape", FontAwesomeIcon.DrawPolygon);

        Row("Kind");
        var shape = P.Shape == CastShape.Ring ? 1 : 0;
        if (Theme.SegmentedControl("##Shape", ref shape, "Bar", "Ring or arc"))
        {
            P.Shape = shape == 1 ? CastShape.Ring : CastShape.Bar;
            changed = true;
        }

        if (P.Shape == CastShape.Bar)
        {
            Row("Fills");
            var dir = (int)P.Direction;
            if (Theme.SegmentedControl("##Direction", ref dir, "Left to right", "Right to left", "Upwards", "Downwards"))
            {
                P.Direction = (FillDirection)dir;
                changed = true;
            }

            FloatRow("Length", P.Length, v => P.Length = v, 40f, 900f, "%.0f px");
            FloatRow("Thickness", P.Thickness, v => P.Thickness = v, 2f, 80f, "%.0f px");
            FloatRow("Corner rounding", P.Rounding, v => P.Rounding = v, 0f, 40f, "%.0f px");
        }
        else
        {
            Row("Arc");
            var preset = P.RingSweep >= 359.5f ? 0 : P.RingSweep >= 179.5f ? 1 : 2;
            if (Theme.SegmentedControl("##ArcPreset", ref preset, "Full circle", "Half", "Crescent"))
            {
                (P.RingSweep, P.RingStart, P.CounterClockwise) = preset switch
                {
                    0 => (360f, 0f, false),
                    1 => (180f, 270f, false),
                    _ => (120f, 240f, true),
                };
                changed = true;
            }

            FloatRow("Radius", P.RingRadius, v => P.RingRadius = v, 12f, 300f, "%.0f px");
            FloatRow("Thickness", P.RingThickness, v => P.RingThickness = v, 1f, 60f, "%.0f px");
            FloatRow("Starts at", P.RingStart, v => P.RingStart = v, 0f, 359f, "%.0f°");
            HelpAfter("Where the fill starts, in degrees clockwise from the top: 90 is the right, 180 the bottom, 270 the left.");
            FloatRow("Covers", P.RingSweep, v => P.RingSweep = v, 30f, 360f, "%.0f°");
            ToggleRow("Fill anticlockwise", P.CounterClockwise, v => P.CounterClockwise = v);
            ToggleRow("Rounded ends", P.Rounding > 0.5f, v => P.Rounding = v ? 4f : 0f, "Round caps on the ends of an arc.");
        }

        FloatRow("Overall size", P.Scale, v => P.Scale = v, 0.3f, 3f, "%.2fx");

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Placement", FontAwesomeIcon.Crosshairs);

        Row("Sits");
        var anchor = (int)P.Anchor;
        if (Theme.SegmentedControl("##Anchor", ref anchor, "On screen", "On my character"))
        {
            P.Anchor = (AnchorMode)anchor;
            P.Offset = Vector2.Zero;
            changed = true;
        }

        if (P.Anchor == AnchorMode.Screen)
        {
            FloatRow("Across", P.ScreenPosition.X, v => P.ScreenPosition = P.ScreenPosition with { X = v }, 0f, 1f, "%.3f");
            FloatRow("Down", P.ScreenPosition.Y, v => P.ScreenPosition = P.ScreenPosition with { Y = v }, 0f, 1f, "%.3f");
            HelpAfter("Where the middle of the castbar sits: 0 is the left or top edge of the screen, 1 the right or bottom.");
        }
        else
        {
            FloatRow("Height", P.CharacterHeight, v => P.CharacterHeight = v, -0.5f, 3.5f, "%.1f yalms");
            HelpAfter("How far above your feet the castbar sits. About 1 is your chest, 2 just over your head.");
            FloatRow("Sideways", P.Offset.X, v => P.Offset = P.Offset with { X = v }, -400f, 400f, "%.0f px");
            FloatRow("Up and down", P.Offset.Y, v => P.Offset = P.Offset with { Y = v }, -400f, 400f, "%.0f px");
        }

        ImGui.Dummy(new Vector2(0, 4));
        if (Theme.GhostButton(plugin.Positioning ? "Done moving" : "Drag it on screen", new Vector2(170, 30), Theme.Crystal))
            plugin.Positioning = !plugin.Positioning;
        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("Reset position", new Vector2(130, 30)))
        {
            var original = BuiltInPresets.All.FirstOrDefault(b => b.Name == P.BasedOn) ?? BuiltInPresets.Default();
            P.ScreenPosition = original.ScreenPosition;
            P.CharacterHeight = original.CharacterHeight;
            P.Offset = original.Offset;
            changed = true;
        }
    }

    // ------------------------------------------------------------------
    // Colours
    // ------------------------------------------------------------------

    private void DrawColours()
    {
        DrawPreview();

        Theme.SectionHeader("By what the spell does", FontAwesomeIcon.Fire);
        foreach (var kind in ActionKindInfo.All)
        {
            var k = kind;
            ColorRow(ActionKindInfo.Name(k), P.Colors.For(k), v => P.Colors.Set(k, v));
            HelpAfter(ActionKindInfo.Blurb(k));
        }

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Can I move?", FontAwesomeIcon.Running);
        ColorRow("Hold still", P.Colors.Lock, v => P.Colors.Lock = v);
        HelpAfter("While moving would cancel the cast.");
        ColorRow("Get ready", P.Colors.Ready, v => P.Colors.Ready = v);
        HelpAfter("The moment just before it's safe. How long that is lives under Timing.");
        ColorRow("Go", P.Colors.Safe, v => P.Colors.Safe = v);
        HelpAfter("The slidecast window: moving now won't cancel anything.");
        ColorRow("Moving too early", P.Colors.Danger, v => P.Colors.Danger = v);
        ColorRow("Queue", P.Colors.Queue, v => P.Colors.Queue = v);
        HelpAfter("The queue window marker and the action you've already queued.");
        ColorRow("Global cooldown", P.Colors.Gcd, v => P.Colors.Gcd = v);
        HelpAfter("After an instant action the castbar shows the global cooldown instead: thinner, without icon or glow, labelled GCD, in this colour.");

        FloatRow("Red while locked", P.LockTint, v => P.LockTint = v, 0f, 1f, "%.2f");
        HelpAfter("How far the fill leans towards the hold-still colour until you can move, then snaps back to the spell's own colour. 0 keeps the spell colour throughout.");
        ToggleRow("Frame shows it too", P.LockFrame, v => P.LockFrame = v,
            "The frame and glow follow hold still, get ready and go. Off keeps them in the frame colour.");
        ToggleRow("Shade the slidecast window", P.ShowSlideZone, v => P.ShowSlideZone = v,
            "Marks the end of the bar where moving becomes safe, with a line where it opens.");

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Frame", FontAwesomeIcon.BorderStyle);
        ColorRow("Background", P.Colors.Background, v => P.Colors.Background = v);
        ColorRow("Frame", P.Colors.Frame, v => P.Colors.Frame = v);
        ColorRow("Text", P.Colors.Text, v => P.Colors.Text = v);
        FloatRow("Frame thickness", P.FrameThickness, v => P.FrameThickness = v, 0f, 5f, "%.1f px");
        FloatRow("Glow", P.Glow, v => P.Glow = v, 0f, 1f, "%.2f");
        FloatRow("Opacity", Config.Opacity, v => Config.Opacity = v, 0.1f, 1f, "%.2f");
    }

    // ------------------------------------------------------------------
    // Text & icon
    // ------------------------------------------------------------------

    private static readonly string[] PlaceNames = ["Hidden", "Inside", "Above", "Below", "Centre"];

    private void DrawTextAndIcon()
    {
        DrawPreview();

        Theme.SectionHeader("Spell icon", FontAwesomeIcon.Image);
        Row("Icon");
        var icon = (int)P.Icon;
        if (Theme.SegmentedControl("##Icon", ref icon, "Hidden", "Before", "After", "Centre"))
        {
            P.Icon = (IconPlace)icon;
            changed = true;
        }
        HelpAfter("Before is left of a bar (below an upright one), after is right (or above). Centre sits inside a ring.");
        if (P.Icon != IconPlace.Hidden)
            FloatRow("Icon size", P.IconSize, v => P.IconSize = v, 12f, 120f, "%.0f px");

        ToggleRow("Show the queued action", P.ShowQueuedIcon, v => P.ShowQueuedIcon = v,
            "Once you've queued your next action, its icon appears next to the castbar.");
        ToggleRow("Mark the queue window", P.ShowQueueMarker, v => P.ShowQueueMarker = v,
            "A tick where the game starts accepting your next action. It lights up once it's open.");

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Text", FontAwesomeIcon.Font);

        Row("Spell name");
        var name = (int)P.NameText;
        if (Theme.SegmentedControl("##Name", ref name, PlaceNames))
        {
            P.NameText = (TextPlace)name;
            changed = true;
        }

        Row("Timer");
        var time = (int)P.TimeText;
        if (Theme.SegmentedControl("##Time", ref time, PlaceNames))
        {
            P.TimeText = (TextPlace)time;
            changed = true;
        }

        if (P.TimeText != TextPlace.Hidden)
        {
            Row("Timer shows");
            var format = (int)P.Time;
            if (Theme.SegmentedControl("##TimeFormat", ref format, "Time left", "Time cast", "Cast / total"))
            {
                P.Time = (TimeFormat)format;
                changed = true;
            }

            Row("Decimals");
            var decimals = P.Decimals;
            if (Theme.SegmentedControl("##Decimals", ref decimals, "0", "1", "2"))
            {
                P.Decimals = decimals;
                changed = true;
            }
        }

        Row("Name font");
        var nameFont = (int)P.NameFont;
        if (Theme.SegmentedControl("##NameFont", ref nameFont, FontService.DisplayName(CastFont.Interface), FontService.DisplayName(CastFont.Jupiter), FontService.DisplayName(CastFont.TrumpGothic)))
        {
            P.NameFont = (CastFont)nameFont;
            changed = true;
        }
        if (FontService.IsLatinOnly(P.NameFont))
            HelpAfter("The game's faces only have Latin letters. If your game is in Japanese, Chinese or Korean, keep Interface for names.");

        Row("Timer font");
        var timeFont = (int)P.TimeFont;
        if (Theme.SegmentedControl("##TimeFont", ref timeFont, FontService.DisplayName(CastFont.Interface), FontService.DisplayName(CastFont.Jupiter), FontService.DisplayName(CastFont.TrumpGothic)))
        {
            P.TimeFont = (CastFont)timeFont;
            changed = true;
        }

        FloatRow("Text size", P.TextScale, v => P.TextScale = v, 0.5f, 2.5f, "%.2fx");
        ToggleRow("Say when to move", P.ShowMoveCue, v => P.ShowMoveCue = v,
            "Once it's safe, the spell name gives way to \"Move\", and the timer turns to the go colour.");
        ToggleRow("Half-second ticks", P.ShowTicks, v => P.ShowTicks = v, "Small marks every half second, longer ones every second.");

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Motion", FontAwesomeIcon.Bolt);
        ToggleRow("Pulse when safe", P.PulseOnSafe, v => P.PulseOnSafe = v, "A quick swell and flash the moment you can move.");
        ToggleRow("Shake when too early", P.ShakeOnDanger, v => P.ShakeOnDanger = v, "The castbar shakes while you move before it's safe.");
        FloatRow("Linger", P.Linger, v => P.Linger = v, 0f, 1.5f, "%.2f s");
        HelpAfter("How long a finished or cancelled cast stays on screen as it fades.");

        ImGui.Dummy(new Vector2(0, 8));
        if (Theme.GhostButton(string.IsNullOrEmpty(P.BasedOn) ? "Reset to default" : $"Reset to {P.BasedOn}", new Vector2(200, 30)))
        {
            var original = BuiltInPresets.All.FirstOrDefault(b => b.Name == P.BasedOn)
                           ?? Config.UserPresets.FirstOrDefault(u => u.Name == P.BasedOn)
                           ?? BuiltInPresets.Default();
            Config.PresetEdits.Remove(P.BasedOn);
            Config.Active = BuiltInPresets.Instantiate(original);
            changed = true;
        }
        if (ImGui.IsItemHovered())
            Theme.Tooltip("Throws away every change you've made to this preset, position included.");
    }

    // ------------------------------------------------------------------
    // Timing
    // ------------------------------------------------------------------

    private void DrawTiming()
    {
        Theme.SectionHeader("Slidecast window", FontAwesomeIcon.Running);
        Wrapped("The game locks a cast in a little before its bar ends, and from then on you can move without cancelling it. "
                + "Half a second is the usual figure. On a slow or distant connection it's shorter, so lower this if casts "
                + "get cancelled when the bar already says go.");

        FloatRow("Window", Config.SlideWindow, v => Config.SlideWindow = v, 0.1f, 0.8f, "%.2f s");
        FloatRow("Get ready", Config.ReadyLead, v => Config.ReadyLead = v, 0f, 1f, "%.2f s");
        HelpAfter("How long before the window opens the bar turns to the get-ready colour. 0 turns the get-ready phase off.");

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Queue window", FontAwesomeIcon.StepForward);
        Wrapped("Press your next action in the last moments before you're free to act, and the game holds it and fires it "
                + "the instant it can. The marker on the bar shows where that starts.");
        FloatRow("Queue window", Config.QueueWindow, v => Config.QueueWindow = v, 0.2f, 0.8f, "%.2f s");

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Warnings", FontAwesomeIcon.ExclamationTriangle);
        ToggleRow("Warn when I move too early", Config.MoveWarning, v => Config.MoveWarning = v,
            "While it isn't safe yet, holding a movement key, pushing the stick or sliding turns the castbar to the warning colour, "
            + "with \"Don't move!\" in place of the name.");
        ToggleRow("Tell me how early I moved", Config.InterruptCoach, v => Config.InterruptCoach = v,
            "When moving cancels a cast, the castbar says by how much you missed the window.");

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("This session", FontAwesomeIcon.ChartLine);
        DrawStats();
    }

    private void DrawStats()
    {
        var stats = plugin.Tracker.Stats;
        var width = (ImGui.GetContentRegionAvail().X - 24f) / 4f;

        Theme.StatCard(FontAwesomeIcon.Magic, "Casts", stats.Casts.ToString(CultureInfo.InvariantCulture), Theme.Gold, width);
        ImGui.SameLine(0, 8);
        Theme.StatCard(FontAwesomeIcon.Running, "Slidecasts", stats.Slides.ToString(CultureInfo.InvariantCulture), Theme.Good, width,
            "Casts that went off while you were already moving.");
        ImGui.SameLine(0, 8);
        Theme.StatCard(FontAwesomeIcon.Ban, "Lost to moving", stats.MovedTooEarly.ToString(CultureInfo.InvariantCulture), Theme.Bad, width,
            "Casts cancelled after you started moving.");
        ImGui.SameLine(0, 8);
        Theme.StatCard(FontAwesomeIcon.Stopwatch, "Average lead", stats.Slides == 0 ? "-" : $"{stats.AverageSlideLead:0.00}s", Theme.Crystal, width,
            "On your slidecasts, how long before the end you started moving. The closer to your window, the more time you got back.");

        ImGui.Dummy(new Vector2(0, 6));
        var suggestion = stats.SuggestedWindow;
        if (!float.IsNaN(suggestion) && suggestion < Config.SlideWindow - 0.01f)
        {
            Theme.Callout(FontAwesomeIcon.Lightbulb, "Your window looks too wide",
                $"A cast was cancelled when you moved {stats.TightestCancel:0.00}s before the end, inside your {Config.SlideWindow:0.00}s window. "
                + $"Your connection probably needs a shorter one, around {suggestion:0.00}s.",
                Theme.Warn);
            ImGui.Dummy(new Vector2(0, 4));
            if (Theme.PrimaryButton($"Use {suggestion:0.00}s", new Vector2(120, 30)))
            {
                Config.SlideWindow = suggestion;
                stats.Reset();
                changed = true;
            }
            ImGui.SameLine(0, 8);
        }

        if (Theme.GhostButton("Reset session", new Vector2(130, 30)))
            stats.Reset();
    }

    // ------------------------------------------------------------------
    // General
    // ------------------------------------------------------------------

    private void DrawGeneral()
    {
        Theme.SectionHeader("Chant", FontAwesomeIcon.PowerOff);

        ToggleRow("Draw the castbar", Config.Enabled, v => Config.Enabled = v, "The master switch. Also /chant toggle.");
        ToggleRow("Hide the game's cast bar", Config.HideGameCastBar, v => Config.HideGameCastBar = v,
            "Hides the game's own cast bar whenever Chant draws in its place. It comes back whenever Chant steps aside: "
            + "turned off, in PvP, or when the plugin is unloaded.");
        ToggleRow("Show the global cooldown", Config.ShowGcd, v => Config.ShowGcd = v,
            "After an instant action, the same shape shows the global cooldown rolling: thinner, in its own colour, without icon or glow "
            + "and labelled GCD, so it never looks like a cast. The queue window is marked on it. "
            + "Handy on jobs that hardly cast.");

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("When to show", FontAwesomeIcon.Eye);
        ToggleRow("Only in combat", Config.OnlyInCombat, v => Config.OnlyInCombat = v,
            "Out of combat, the game's own cast bar takes over again.");
        ToggleRow("Skip everyday casts", Config.HideUtilityCasts, v => Config.HideUtilityCasts = v,
            "Leave mounts, items, teleports, crafting and gathering to the game's own cast bar.");

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Party", FontAwesomeIcon.Users);
        ToggleRow("Raise watch", Config.RaiseWatch, v => Config.RaiseWatch = v,
            "When someone in your party is raising, a small tag under the castbar says who is raising whom, and how long is left. "
            + "It turns gold when it's your current target, so you don't raise them twice.");
        if (Config.RaiseWatch)
        {
            Row("Sits");
            var placement = (int)Config.RaiseWatchPlacement;
            if (Theme.SegmentedControl("##RaisePlacement", ref placement, "Under the castbar", "Where I put it"))
            {
                Config.RaiseWatchPlacement = (RaiseWatchPlacement)placement;
                changed = true;
            }
            HelpAfter("Press Move castbar at the top: the raise watch gets its own handle, and dragging it frees it from the castbar.");

            if (Config.RaiseWatchPlacement == RaiseWatchPlacement.Free)
            {
                FloatRow("Across", Config.RaiseWatchPosition.X, v => Config.RaiseWatchPosition = Config.RaiseWatchPosition with { X = v }, 0f, 1f, "%.3f");
                FloatRow("Down", Config.RaiseWatchPosition.Y, v => Config.RaiseWatchPosition = Config.RaiseWatchPosition with { Y = v }, 0f, 1f, "%.3f");
            }
        }

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Sound", FontAwesomeIcon.VolumeUp);
        ToggleRow("Chime when safe", Config.SafeChime, v => Config.SafeChime = v,
            "Plays one of the game's chat sounds the moment you can move.");
        if (Config.SafeChime)
        {
            Row("Sound");
            var sound = Config.SafeChimeSound;
            ImGui.SetNextItemWidth(ControlWidth);
            if (Theme.SliderInt("##Sound", ref sound, 1, 16, ControlWidth))
            {
                Config.SafeChimeSound = sound;
                changed = true;
            }
            ImGui.SameLine(0, 8);
            if (Theme.IconButton("##PlaySound", FontAwesomeIcon.Play, "Play it", Theme.Gold, false, 26f))
                Plugin.PlayChime(Config.SafeChimeSound);
            HelpAfter("The <se.1> to <se.16> sounds from chat. It plays at the game's sound effect volume.");

            FloatRow("For casts from", Config.SafeChimeMinCast, v => Config.SafeChimeMinCast = v, 0f, 6f, "%.1f s");
            HelpAfter("Shorter casts stay quiet, so the chime is there for the long ones (raises, big heals).");
        }

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Motion", FontAwesomeIcon.Running);

        Row("Reduced motion");
        var motion = (int)Config.ReducedMotion;
        if (Theme.SegmentedControl("##Motion", ref motion, "Follow Dalamud", "On", "Off"))
        {
            Config.ReducedMotion = (ReducedMotionMode)motion;
            changed = true;
        }
        HelpAfter("When on, the castbar doesn't pulse, swell or shake. Colours still change.");

        ImGui.Dummy(new Vector2(0, 10));
        Theme.Callout(FontAwesomeIcon.ShieldAlt, "Purely visual",
            "Chant reads the cast information the game already shows you and draws over the screen. "
            + "It never casts, queues or moves anything for you, and it steps aside in PvP, cutscenes and GPose.",
            Theme.Good);
    }

    // ------------------------------------------------------------------
    // Diagnostics
    // ------------------------------------------------------------------

    private void DrawDiagnostics()
    {
        Theme.SectionHeader("Status", FontAwesomeIcon.Heartbeat);
        DrawStatus();

        ImGui.Dummy(new Vector2(0, 8));
        var logEvents = Config.LogEvents;
        if (Theme.ToggleRow("Log casts", ref logEvents,
                "Writes every cast to the log below: its id, how Chant classified it and why, and how it ended. "
                + "Useful when a spell gets the wrong colour, or a cancel isn't explained. Leave it off otherwise."))
        {
            Config.LogEvents = logEvents;
            ConfigService.Save();
        }

        ImGui.Dummy(new Vector2(0, 8));
        Theme.SectionHeader("Feedback", FontAwesomeIcon.CommentDots);

        Theme.Callout(FontAwesomeIcon.Bug, "Something not working?",
            "1. Turn on Log casts, make the problem happen again, then come back to this page.\n" +
            "2. Press Copy report. It copies your Chant version, a few settings, the last cast and the log below. " +
            "Click individual lines first if you only want to share those.\n" +
            "3. Press Report a bug, tell me what you did, what you expected and what happened instead, " +
            "then paste the report into the issue.",
            Theme.Warn);

        ImGui.Dummy(new Vector2(0, 6));
        Theme.Callout(FontAwesomeIcon.Lightbulb, "Have an idea?",
            "Issues are also the place for feature requests. Press Suggest a feature and describe what you'd " +
            "like Chant to do and how it would help you. No log needed.\n" +
            "For quick questions, or to talk ideas through first, join the Discord.",
            Theme.Crystal);

        ImGui.Dummy(new Vector2(0, 8));
        if (Theme.PrimaryButton("Report a bug", new Vector2(140, 32)))
            OpenUrl(NewIssueUrl("bug", "[Bug] ",
                "**What happened?**\n\n\n**What did you expect?**\n\n\n**Steps to reproduce**\n1. \n\n" +
                "**Diagnostics report**\n<!-- Settings → Diagnostics → Copy report, then paste here -->\n"));

        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("Suggest a feature", new Vector2(150, 32), Theme.Crystal))
            OpenUrl(NewIssueUrl("enhancement", "[Feature] ",
                "**What would you like Chant to do?**\n\n\n**How would it help you?**\n\n"));

        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("Open issues", new Vector2(120, 32)))
            OpenUrl($"{Plugin.RepoUrl}/issues");

        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("Discord", new Vector2(100, 32), Theme.DiscordBlurple))
            OpenUrl(Plugin.DiscordUrl);

        ImGui.Dummy(new Vector2(0, 12));
        Theme.SectionHeader("Log", FontAwesomeIcon.Terminal);

        RefreshLogView();

        if (Theme.SegmentedControl("##LogLevel", ref logLevelFilter, "All", "Info", "Warnings", "Errors"))
            RefreshLogView();

        ImGui.SameLine(0, 10);
        ImGui.SetNextItemWidth(Math.Max(ImGui.GetContentRegionAvail().X, 120f));
        if (ImGui.InputTextWithHint("##LogSearch", "Filter log lines", ref logSearch, 100))
            RefreshLogView();

        ImGui.Dummy(new Vector2(0, 4));
        var copyLabel = selectedLogLines.Count > 0 ? $"Copy report ({selectedLogLines.Count})" : "Copy report";
        if (Theme.PrimaryButton(copyLabel, new Vector2(150, 30)))
            ImGui.SetClipboardText(BuildReport());
        if (ImGui.IsItemHovered())
            Theme.Tooltip(selectedLogLines.Count > 0
                ? "Copies your setup and the selected lines, ready to paste into an issue."
                : $"Copies your setup and the last {ReportLineLimit} lines shown, ready to paste into an issue.");

        ImGui.SameLine(0, 8);
        if (Theme.GhostButton("Copy lines only", new Vector2(130, 30)))
            ImGui.SetClipboardText(string.Join("\n", ReportLines().Select(e => e.Format())));

        if (selectedLogLines.Count > 0)
        {
            ImGui.SameLine(0, 8);
            if (Theme.GhostButton("Deselect", new Vector2(90, 30)))
                selectedLogLines.Clear();
        }

        ImGui.SameLine(0, 8);
        if (Theme.DangerButton("Clear", new Vector2(70, 30)))
        {
            Plugin.Log.Clear();
            selectedLogLines.Clear();
            RefreshLogView();
        }

        ImGui.SameLine(0, 12);
        ImGui.AlignTextToFramePadding();
        ImGui.Checkbox("Auto-scroll", ref logAutoScroll);

        ImGui.Dummy(new Vector2(0, 4));
        DrawLogLines();
    }

    private void DrawStatus()
    {
        var suppressed = plugin.Overlay.SuppressedReason;
        var width = (ImGui.GetContentRegionAvail().X - 16f) / 3f;
        var frame = plugin.Tracker.Frame;
        var info = plugin.Tracker.LastInfo;
        var (type, id) = plugin.Tracker.LastAction;

        Theme.StatCard(suppressed is null ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.EyeSlash, "Drawing",
            suppressed is null ? "Yes" : "No", suppressed is null ? Theme.Good : Theme.Warn, width,
            suppressed is null ? null : $"Not drawing: {suppressed}.");
        ImGui.SameLine(0, 8);
        Theme.StatCard(FontAwesomeIcon.Magic, "Now", frame.Phase == CastPhase.None ? "-" : frame.Phase.ToString(), Theme.Gold, width,
            frame.Visible ? $"{frame.Name}: {frame.Elapsed:0.00} of {frame.Total:0.00}s" : null);
        ImGui.SameLine(0, 8);
        Theme.StatCard(FontAwesomeIcon.EyeSlash, "Game cast bar", GameCastBar.HiddenByUs ? "Hidden" : "Shown", Theme.Crystal, width);

        ImGui.Dummy(new Vector2(0, 6));
        if (string.IsNullOrEmpty(info.Name))
        {
            ImGui.TextColored(Theme.TextFaint, "No cast seen yet. Cast something, or press Test casts at the top.");
            return;
        }

        ImGui.TextColored(Theme.TextMuted, $"Last cast: {info.Name} ({type} {id}), {ActionKindInfo.Name(info.Kind).ToLowerInvariant()} because {info.Why}.");
        ImGui.TextColored(Theme.TextMuted, $"Moving: keys {OnOff(MovementSense.KeyHeld)}, stick {OnOff(MovementSense.StickHeld)}, position {OnOff(MovementSense.Displaced)}.");
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    private void DrawLogLines()
    {
        var height = Math.Max(ImGui.GetContentRegionAvail().Y - 10f, 220f);
        using var card = Theme.Card("##LogCard", new Vector2(0, height));
        if (!card) return;

        using var rows = Theme.Region("##LogRows", Vector2.Zero, ImGuiWindowFlags.HorizontalScrollbar);
        if (!rows) return;

        if (visibleLogLines.Count == 0)
        {
            ImGui.TextColored(Theme.TextFaint, logViewKey.Level == 0 && logSearch.Length == 0
                ? "Nothing logged yet."
                : "No lines match the current filter.");
            return;
        }

        for (var i = 0; i < visibleLogLines.Count; i++)
        {
            var entry = visibleLogLines[i];
            var selected = selectedLogLines.Contains(entry);

            using (ImRaii.PushColor(ImGuiCol.Text, LevelColor(entry.Level)))
            {
                if (ImGui.Selectable($"{entry.Format()}##log{i}", selected))
                {
                    if (!selectedLogLines.Remove(entry))
                        selectedLogLines.Add(entry);
                }
            }

            using var popup = ImRaii.ContextPopupItem($"##logctx{i}");
            if (popup.Success && ImGui.Selectable("Copy this line"))
                ImGui.SetClipboardText(entry.Format());
        }

        if (logAutoScroll && scrolledRevision != Plugin.Log.Revision)
        {
            ImGui.SetScrollHereY(1f);
            scrolledRevision = Plugin.Log.Revision;
        }
    }

    private void RefreshLogView()
    {
        var key = (Plugin.Log.Revision, logLevelFilter, logSearch);
        if (key == logViewKey) return;
        logViewKey = key;

        var minLevel = (LogLevel)logLevelFilter;
        visibleLogLines = Plugin.Log.Snapshot()
            .Where(e => e.Level >= minLevel)
            .Where(e => logSearch.Length == 0
                        || e.Message.Contains(logSearch, StringComparison.OrdinalIgnoreCase)
                        || (e.Exception?.Contains(logSearch, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        // Lines that scrolled out of the buffer can't be copied any more.
        var all = Plugin.Log.Snapshot().ToHashSet();
        selectedLogLines.RemoveWhere(e => !all.Contains(e));
    }

    private IEnumerable<LogEntry> ReportLines()
    {
        return selectedLogLines.Count > 0
            ? selectedLogLines.OrderBy(e => e.Time)
            : visibleLogLines.Skip(Math.Max(0, visibleLogLines.Count - ReportLineLimit));
    }

    private string BuildReport()
    {
        var sb = new StringBuilder();
        var stats = plugin.Tracker.Stats;
        var info = plugin.Tracker.LastInfo;
        var (type, id) = plugin.Tracker.LastAction;

        sb.AppendLine("### Chant diagnostics");
        sb.AppendLine($"- Chant: {Changelog.CurrentVersion}");
        sb.AppendLine($"- Dalamud: {typeof(Dalamud.Plugin.IDalamudPluginInterface).Assembly.GetName().Version}");
        sb.AppendLine($"- Game language: {Plugin.ClientState.ClientLanguage}, territory {Plugin.ClientState.TerritoryType}");
        sb.AppendLine($"- Settings: enabled {OnOff(Config.Enabled)}, preset {P.BasedOn} ({P.Shape}, {P.Anchor}), slide {Config.SlideWindow:0.00}s, "
                      + $"ready {Config.ReadyLead:0.00}s, queue {Config.QueueWindow:0.00}s, hide game bar {OnOff(Config.HideGameCastBar)}, "
                      + $"gcd {OnOff(Config.ShowGcd)}, combat only {OnOff(Config.OnlyInCombat)}, raise watch {OnOff(Config.RaiseWatch)}");
        sb.AppendLine($"- State: suppressed {plugin.Overlay.SuppressedReason ?? "no"}, frame errors {plugin.FrameErrors}, "
                      + $"game bar hidden {OnOff(GameCastBar.HiddenByUs)}");
        sb.AppendLine($"- Last cast: {info.Name} ({type} {id}) as {info.Kind} because {info.Why}");
        sb.AppendLine($"- Session: {stats.Casts} casts, {stats.Completed} went off, {stats.Interrupted} cancelled "
                      + $"({stats.MovedTooEarly} after moving), {stats.Slides} slides");
        sb.AppendLine($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine("```text");
        foreach (var entry in ReportLines())
            sb.AppendLine(entry.Format());
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static Vector4 LevelColor(LogLevel level) => level switch
    {
        LogLevel.Error => Theme.Bad,
        LogLevel.Warning => Theme.Warn,
        LogLevel.Info => Theme.Text,
        _ => Theme.TextMuted,
    };

    private static string NewIssueUrl(string label, string title, string body)
        => $"{Plugin.RepoUrl}/issues/new?labels={label}&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";

    // ------------------------------------------------------------------
    // About
    // ------------------------------------------------------------------

    private void DrawAbout()
    {
        Theme.SectionHeader("Chant", FontAwesomeIcon.Magic);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextMuted, $"Version {Changelog.CurrentVersion}  ·  by GitHixy");
        ImGui.SameLine(0, 12);
        if (Theme.GhostButton("What's new", new Vector2(120, 0), Theme.Gold))
            plugin.ChangelogWindow.ShowAll();

        ImGui.Dummy(new Vector2(0, 6));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Min(ImGui.GetContentRegionAvail().X, 460f));
        ImGui.TextColored(Theme.Text,
            "A castbar built to be read at a glance: red while moving would cancel your cast, a flash the moment you can go, " +
            "and your next action already lined up beside it.");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0, 14));
        Theme.SectionHeader("Support", FontAwesomeIcon.Heart);

        using (ImRaii.PushColor(ImGuiCol.Button, Theme.Alpha(Theme.PatreonCoral, 0.85f))
                   .Push(ImGuiCol.ButtonHovered, Theme.Lighten(Theme.PatreonCoral, 0.12f))
                   .Push(ImGuiCol.ButtonActive, new Vector4(0.85f, 0.20f, 0.24f, 1.0f))
                   .Push(ImGuiCol.Text, Theme.Text))
        {
            if (ImGui.Button("Support me on Patreon", new Vector2(230, 34)))
                OpenUrl(Plugin.PatreonUrl);
        }

        ImGui.SameLine(0, 10);
        if (Theme.GhostButton("Join the Discord", new Vector2(150, 34), Theme.DiscordBlurple))
            OpenUrl(Plugin.DiscordUrl);

        ImGui.SameLine(0, 10);
        if (Theme.GhostButton("GitHub", new Vector2(110, 34)))
            OpenUrl(Plugin.RepoUrl);

        ImGui.Dummy(new Vector2(0, 2));
        if (Theme.GhostButton("Report a problem", new Vector2(150, 30)))
            section = Array.FindIndex(Sections, s => s.Label == "Diagnostics");

        ImGui.Dummy(new Vector2(0, 14));
        Theme.SectionHeader("Shortcuts", FontAwesomeIcon.Keyboard);

        ShortcutRow("/chant", "Open these settings");
        ShortcutRow("/chant test", "Play sample casts on screen");
        ShortcutRow("/chant move", "Drag the castbar into place");
        ShortcutRow("/chant toggle", "Turn the castbar on or off");

        ImGui.Dummy(new Vector2(0, 14));
        Theme.SectionHeader("Also by GitHixy", FontAwesomeIcon.Cubes);
        ShortcutRow("LootView", "Live loot tracker with history and statistics");
        ShortcutRow("HitSpark", "Animated, styleable combat text");
        ShortcutRow("FateLimit", "See the edge of every FATE");
        ShortcutRow("MoogleMap", "A see-through map over your screen");
    }

    private static void ShortcutRow(string command, string description)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var ts = ImGui.CalcTextSize(command);

        dl.AddRectFilled(p, new Vector2(p.X + ts.X + 14, p.Y + ts.Y + 5), Theme.U32(Theme.Panel, 0.95f), 4f);
        dl.AddRect(p, new Vector2(p.X + ts.X + 14, p.Y + ts.Y + 5), Theme.U32(Theme.Line), 4f, ImDrawFlags.None, 1f);
        dl.AddText(new Vector2(p.X + 7, p.Y + 2.5f), Theme.U32(Theme.GoldBright), command);

        ImGui.Dummy(new Vector2(Math.Max(ts.X + 14, 150f), ts.Y + 5));
        ImGui.SameLine(0, 12);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextMuted, description);
        ImGui.Dummy(new Vector2(0, 2));
    }

    // ------------------------------------------------------------------
    // Setting rows
    // ------------------------------------------------------------------

    private static void Row(string label)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextMuted, label);
        ImGui.SameLine(LabelWidth);
    }

    private static void Wrapped(string text, Vector4? color = null)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Min(ImGui.GetContentRegionAvail().X, 560f));
        ImGui.TextColored(color ?? Theme.TextMuted, text);
        ImGui.PopTextWrapPos();
    }

    private static void HelpAfter(string help)
    {
        ImGui.SameLine(0, 8);
        Theme.HelpMarker(help);
    }

    private void FloatRow(string label, float value, Action<float> set, float min, float max, string format)
    {
        Row(label);
        var v = value;
        if (Theme.Slider($"##{label}", ref v, min, max, format, ControlWidth))
        {
            set(Math.Clamp(v, min, max));
            changed = true;
        }
    }

    private void ToggleRow(string label, bool value, Action<bool> set, string? help = null)
    {
        Row(label);
        var v = value;
        if (Theme.Toggle($"##tgl_{label}", ref v))
        {
            set(v);
            changed = true;
        }

        if (help is not null)
            HelpAfter(help);
    }

    private void ColorRow(string label, Vector4 value, Action<Vector4> set)
    {
        Row(label);
        var v = value;
        if (ImGui.ColorEdit4($"##{label}", ref v, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            set(v);
            changed = true;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to open {Url}", url);
        }
    }
}
