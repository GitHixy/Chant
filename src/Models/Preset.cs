using System;
using System.Numerics;
using Newtonsoft.Json;

namespace Chant.Models;

public enum CastShape
{
    /// <summary>A straight bar, horizontal or vertical.</summary>
    Bar,
    /// <summary>A ring or any part of one, from a full circle down to a shallow arc.</summary>
    Ring,
}

public enum FillDirection
{
    LeftToRight,
    RightToLeft,
    BottomToTop,
    TopToBottom,
}

public enum AnchorMode
{
    /// <summary>A fixed point on the screen.</summary>
    Screen,
    /// <summary>Follows your character around, at a chosen height above the ground.</summary>
    Character,
}

/// <summary>Where a piece of text sits relative to the shape.</summary>
public enum TextPlace
{
    Hidden,
    Inside,
    Above,
    Below,
    Center,
}

public enum IconPlace
{
    Hidden,
    /// <summary>Before the bar (left, or below a vertical bar).</summary>
    Start,
    /// <summary>After the bar (right, or above a vertical bar).</summary>
    End,
    /// <summary>In the middle: inside a ring, or over the bar.</summary>
    Center,
}

public enum CastFont
{
    /// <summary>Dalamud's interface font, with every glyph your game language needs.</summary>
    Interface,
    /// <summary>The game's serif face.</summary>
    Jupiter,
    /// <summary>The game's condensed face, the one its own numbers use.</summary>
    TrumpGothic,
}

public enum TimeFormat
{
    Remaining,
    Elapsed,
    ElapsedOfTotal,
}

/// <summary>Every colour a preset uses.</summary>
[Serializable]
public class Palette
{
    public Vector4 Damage { get; set; } = new(0.96f, 0.55f, 0.30f, 1f);
    public Vector4 Heal { get; set; } = new(0.42f, 0.88f, 0.55f, 1f);
    public Vector4 Raise { get; set; } = new(1.00f, 0.86f, 0.45f, 1f);
    public Vector4 Support { get; set; } = new(0.45f, 0.75f, 1.00f, 1f);
    public Vector4 Utility { get; set; } = new(0.78f, 0.72f, 0.90f, 1f);

    /// <summary>While moving would cancel the cast.</summary>
    public Vector4 Lock { get; set; } = new(0.92f, 0.26f, 0.24f, 1f);
    /// <summary>The moment just before it's safe to move.</summary>
    public Vector4 Ready { get; set; } = new(1.00f, 0.72f, 0.25f, 1f);
    /// <summary>Safe to move: the slidecast window.</summary>
    public Vector4 Safe { get; set; } = new(0.40f, 0.95f, 0.60f, 1f);
    /// <summary>The queue window and the queued action.</summary>
    public Vector4 Queue { get; set; } = new(0.45f, 0.82f, 1.00f, 1f);
    /// <summary>Moving while it isn't safe yet.</summary>
    public Vector4 Danger { get; set; } = new(1.00f, 0.18f, 0.18f, 1f);
    /// <summary>The global cooldown after instant actions, drawn apart from casts.</summary>
    public Vector4 Gcd { get; set; } = new(0.72f, 0.80f, 0.92f, 0.75f);

    public Vector4 Background { get; set; } = new(0.03f, 0.05f, 0.09f, 0.78f);
    public Vector4 Frame { get; set; } = new(0.84f, 0.69f, 0.41f, 0.85f);
    public Vector4 Text { get; set; } = new(0.97f, 0.94f, 0.87f, 1f);

    public Vector4 For(ActionKind kind) => kind switch
    {
        ActionKind.Damage => Damage,
        ActionKind.Heal => Heal,
        ActionKind.Raise => Raise,
        ActionKind.Support => Support,
        _ => Utility,
    };

    public void Set(ActionKind kind, Vector4 value)
    {
        switch (kind)
        {
            case ActionKind.Damage: Damage = value; break;
            case ActionKind.Heal: Heal = value; break;
            case ActionKind.Raise: Raise = value; break;
            case ActionKind.Support: Support = value; break;
            default: Utility = value; break;
        }
    }

    public Palette Clone() => (Palette)MemberwiseClone();
}

/// <summary>
/// A complete look for the castbar: shape, placement, colours, text and motion. Built-in presets
/// are templates; the one in use is the user's own copy, freely edited.
/// </summary>
[Serializable]
public class Preset
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom";
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    /// <summary>The built-in preset this one started from, for display only.</summary>
    public string BasedOn { get; set; } = string.Empty;
    [JsonIgnore] public bool IsBuiltIn { get; set; }

    // Shape
    public CastShape Shape { get; set; } = CastShape.Bar;
    /// <summary>Bar length along its fill direction, at 1080p.</summary>
    public float Length { get; set; } = 320f;
    /// <summary>Bar thickness across its fill direction.</summary>
    public float Thickness { get; set; } = 18f;
    public FillDirection Direction { get; set; } = FillDirection.LeftToRight;
    public float Rounding { get; set; } = 4f;
    public float RingRadius { get; set; } = 46f;
    public float RingThickness { get; set; } = 8f;
    /// <summary>Where the ring starts, in degrees clockwise from twelve o'clock.</summary>
    public float RingStart { get; set; } = 0f;
    /// <summary>How much of the circle the ring covers, in degrees.</summary>
    public float RingSweep { get; set; } = 360f;
    public bool CounterClockwise { get; set; }

    // Placement
    public AnchorMode Anchor { get; set; } = AnchorMode.Screen;
    /// <summary>Centre of the castbar as a fraction of the screen, for <see cref="AnchorMode.Screen"/>.</summary>
    public Vector2 ScreenPosition { get; set; } = new(0.5f, 0.70f);
    /// <summary>Height above your feet in yalms, for <see cref="AnchorMode.Character"/>.</summary>
    public float CharacterHeight { get; set; } = 0f;
    /// <summary>Pixel offset from the anchor, at 1080p.</summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;
    public float Scale { get; set; } = 1f;

    // Look
    public Palette Colors { get; set; } = new();
    public float FrameThickness { get; set; } = 1.5f;
    /// <summary>Soft halo around the shape, 0 to 1.</summary>
    public float Glow { get; set; } = 0.5f;
    /// <summary>How far the fill leans to the lock colour while moving isn't safe, 0 to 1.</summary>
    public float LockTint { get; set; } = 0.55f;
    /// <summary>Frame and glow follow lock, ready and safe colours.</summary>
    public bool LockFrame { get; set; } = true;
    /// <summary>Shade the end of the bar where moving becomes safe.</summary>
    public bool ShowSlideZone { get; set; } = true;
    /// <summary>A tick where the queue window opens.</summary>
    public bool ShowQueueMarker { get; set; } = true;
    /// <summary>The icon of the action you've already queued, next to the bar.</summary>
    public bool ShowQueuedIcon { get; set; } = true;
    /// <summary>Small ticks every half second.</summary>
    public bool ShowTicks { get; set; }
    /// <summary>A "MOVE" cue when it's safe.</summary>
    public bool ShowMoveCue { get; set; } = true;

    // Icon and text
    public IconPlace Icon { get; set; } = IconPlace.Start;
    public float IconSize { get; set; } = 34f;
    public TextPlace NameText { get; set; } = TextPlace.Above;
    public TextPlace TimeText { get; set; } = TextPlace.Inside;
    public TimeFormat Time { get; set; } = TimeFormat.Remaining;
    public int Decimals { get; set; } = 1;
    public float TextScale { get; set; } = 1f;
    public CastFont NameFont { get; set; } = CastFont.Interface;
    public CastFont TimeFont { get; set; } = CastFont.TrumpGothic;

    // Motion
    /// <summary>A flash and swell the moment it becomes safe to move.</summary>
    public bool PulseOnSafe { get; set; } = true;
    /// <summary>Shake while you move too early.</summary>
    public bool ShakeOnDanger { get; set; } = true;
    /// <summary>How long a finished or cancelled cast lingers, in seconds.</summary>
    public float Linger { get; set; } = 0.45f;

    public bool IsVertical => Shape == CastShape.Bar && Direction is FillDirection.BottomToTop or FillDirection.TopToBottom;

    public Preset Clone()
    {
        var copy = (Preset)MemberwiseClone();
        copy.Colors = Colors.Clone();
        return copy;
    }

    /// <summary>Clamps every number into the range the editor allows, for imported codes.</summary>
    public void Clamp()
    {
        Length = Math.Clamp(Length, 40f, 1200f);
        Thickness = Math.Clamp(Thickness, 2f, 120f);
        Rounding = Math.Clamp(Rounding, 0f, 60f);
        RingRadius = Math.Clamp(RingRadius, 12f, 400f);
        RingThickness = Math.Clamp(RingThickness, 1f, 80f);
        RingStart = ((RingStart % 360f) + 360f) % 360f;
        RingSweep = Math.Clamp(RingSweep, 30f, 360f);
        ScreenPosition = Vector2.Clamp(ScreenPosition, Vector2.Zero, Vector2.One);
        CharacterHeight = Math.Clamp(CharacterHeight, -1f, 4f);
        Offset = Vector2.Clamp(Offset, new Vector2(-1500f), new Vector2(1500f));
        Scale = Math.Clamp(Scale, 0.3f, 4f);
        FrameThickness = Math.Clamp(FrameThickness, 0f, 6f);
        Glow = Math.Clamp(Glow, 0f, 1f);
        LockTint = Math.Clamp(LockTint, 0f, 1f);
        IconSize = Math.Clamp(IconSize, 12f, 160f);
        Decimals = Math.Clamp(Decimals, 0, 2);
        TextScale = Math.Clamp(TextScale, 0.5f, 3f);
        Linger = Math.Clamp(Linger, 0f, 2f);
        if (!Enum.IsDefined(Shape)) Shape = CastShape.Bar;
        if (!Enum.IsDefined(Direction)) Direction = FillDirection.LeftToRight;
        if (!Enum.IsDefined(Anchor)) Anchor = AnchorMode.Screen;
        if (!Enum.IsDefined(Icon)) Icon = IconPlace.Start;
        if (!Enum.IsDefined(NameText)) NameText = TextPlace.Above;
        if (!Enum.IsDefined(TimeText)) TimeText = TextPlace.Inside;
        if (!Enum.IsDefined(Time)) Time = TimeFormat.Remaining;
        if (!Enum.IsDefined(NameFont)) NameFont = CastFont.Interface;
        if (!Enum.IsDefined(TimeFont)) TimeFont = CastFont.TrumpGothic;
    }
}
