using System;
using System.Collections.Generic;
using System.Numerics;
using Chant.Models;

namespace Chant.Presets;

/// <summary>
/// The presets that ship with Chant. Each is a complete look; picking one copies it into the
/// configuration, where it can be edited without touching the original.
/// </summary>
public static class BuiltInPresets
{
    public const string DefaultId = "builtin.aether";

    public static readonly IReadOnlyList<Preset> All =
    [
        Aether(),
        Sigil(),
        Halo(),
        Pillar(),
        Crescent(),
        Runic(),
        Whisper(),
    ];

    /// <summary>A fresh, editable copy of the default preset.</summary>
    public static Preset Default() => Instantiate(Aether());

    /// <summary>An editable copy of any preset, remembering where it came from.</summary>
    public static Preset Instantiate(Preset source)
    {
        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.IsBuiltIn = false;
        copy.BasedOn = source.Name;
        return copy;
    }

    private static Vector4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    /// <summary>A brass-framed bar under your character, icon on the left. The all-rounder.</summary>
    private static Preset Aether() => new()
    {
        Id = DefaultId,
        Name = "Aether",
        Description = "A brass-framed bar below the centre of the screen, with your spell's icon on the left and its name above.",
        Author = "GitHixy",
        IsBuiltIn = true,
        Shape = CastShape.Bar,
        Length = 320f,
        Thickness = 18f,
        Rounding = 4f,
        ScreenPosition = new Vector2(0.5f, 0.70f),
        Icon = IconPlace.Start,
        IconSize = 34f,
        NameText = TextPlace.Above,
        TimeText = TextPlace.Inside,
        Glow = 0.5f,
        LockTint = 0.55f,
    };

    /// <summary>A full ring around the spell's icon.</summary>
    private static Preset Sigil() => new()
    {
        Id = "builtin.sigil",
        Name = "Sigil",
        Description = "A ring around your spell's icon that fills like a clock. The slidecast window is the last slice of the circle.",
        Author = "GitHixy",
        IsBuiltIn = true,
        Shape = CastShape.Ring,
        RingRadius = 40f,
        RingThickness = 7f,
        RingStart = 0f,
        RingSweep = 360f,
        ScreenPosition = new Vector2(0.5f, 0.66f),
        Icon = IconPlace.Center,
        IconSize = 50f,
        NameText = TextPlace.Below,
        TimeText = TextPlace.Below,
        Glow = 0.65f,
        LockTint = 0.5f,
        Colors = new Palette
        {
            Frame = Rgb(0x9E, 0xD9, 0xF5, 0.8f),
            Background = Rgb(0x05, 0x0A, 0x14, 0.72f),
        },
    };

    /// <summary>A thin ring around your character that follows you.</summary>
    private static Preset Halo() => new()
    {
        Id = "builtin.halo",
        Name = "Halo",
        Description = "A thin ring of light around your character that follows you everywhere. Nothing else on screen, eyes stay on the fight.",
        Author = "GitHixy",
        IsBuiltIn = true,
        Shape = CastShape.Ring,
        RingRadius = 74f,
        RingThickness = 4f,
        RingStart = 0f,
        RingSweep = 360f,
        Anchor = AnchorMode.Character,
        CharacterHeight = 1.0f,
        Icon = IconPlace.Hidden,
        NameText = TextPlace.Hidden,
        TimeText = TextPlace.Below,
        TextScale = 0.9f,
        FrameThickness = 0f,
        Glow = 0.8f,
        LockTint = 0.7f,
        ShowQueuedIcon = true,
        Colors = new Palette
        {
            Background = Rgb(0x00, 0x00, 0x00, 0.35f),
        },
    };

    /// <summary>A vertical gauge standing next to your character.</summary>
    private static Preset Pillar() => new()
    {
        Id = "builtin.pillar",
        Name = "Pillar",
        Description = "An upright gauge beside your character that fills from the bottom, with the icon on top. Out of the way of the action, close to your eyes.",
        Author = "GitHixy",
        IsBuiltIn = true,
        Shape = CastShape.Bar,
        Length = 140f,
        Thickness = 12f,
        Rounding = 6f,
        Direction = FillDirection.BottomToTop,
        Anchor = AnchorMode.Character,
        CharacterHeight = 1.0f,
        Offset = new Vector2(78f, 0f),
        Icon = IconPlace.End,
        IconSize = 30f,
        NameText = TextPlace.Hidden,
        TimeText = TextPlace.Below,
        TextScale = 0.95f,
        Glow = 0.55f,
        LockTint = 0.6f,
        Colors = new Palette
        {
            Frame = Rgb(0xE7, 0xE1, 0xD3, 0.55f),
        },
    };

    /// <summary>A shallow arc cradling the icon.</summary>
    private static Preset Crescent() => new()
    {
        Id = "builtin.crescent",
        Name = "Crescent",
        Description = "A curved bar that cradles your spell's icon like a crescent moon, filling from left to right.",
        Author = "GitHixy",
        IsBuiltIn = true,
        Shape = CastShape.Ring,
        RingRadius = 84f,
        RingThickness = 9f,
        RingStart = 240f,
        RingSweep = 120f,
        CounterClockwise = true,
        ScreenPosition = new Vector2(0.5f, 0.62f),
        Icon = IconPlace.Center,
        IconSize = 40f,
        NameText = TextPlace.Below,
        TimeText = TextPlace.Below,
        Glow = 0.55f,
        LockTint = 0.5f,
        Colors = new Palette
        {
            Frame = Rgb(0xF2, 0xD7, 0x96, 0.7f),
        },
    };

    /// <summary>A long segmented bar with ticks every half second.</summary>
    private static Preset Runic() => new()
    {
        Id = "builtin.runic",
        Name = "Runic",
        Description = "A long, sharp bar etched every half second, with everything written inside it. For counting the rhythm of long casts.",
        Author = "GitHixy",
        IsBuiltIn = true,
        Shape = CastShape.Bar,
        Length = 420f,
        Thickness = 22f,
        Rounding = 0f,
        ScreenPosition = new Vector2(0.5f, 0.74f),
        Icon = IconPlace.Start,
        IconSize = 30f,
        NameText = TextPlace.Inside,
        TimeText = TextPlace.Inside,
        Time = TimeFormat.ElapsedOfTotal,
        ShowTicks = true,
        FrameThickness = 1f,
        Glow = 0.35f,
        LockTint = 0.45f,
        Colors = new Palette
        {
            Damage = Rgb(0xC4, 0x7B, 0xFF),
            Heal = Rgb(0x5E, 0xE6, 0xB8),
            Raise = Rgb(0xFF, 0xE0, 0x8A),
            Support = Rgb(0x6E, 0xA8, 0xFF),
            Frame = Rgb(0xB9, 0x9C, 0xFF, 0.7f),
            Background = Rgb(0x0C, 0x08, 0x18, 0.82f),
        },
    };

    /// <summary>A hairline with nothing else.</summary>
    private static Preset Whisper() => new()
    {
        Id = "builtin.whisper",
        Name = "Whisper",
        Description = "A hairline just below the middle of the screen. Red means stay, a flash means go. Nothing else.",
        Author = "GitHixy",
        IsBuiltIn = true,
        Shape = CastShape.Bar,
        Length = 240f,
        Thickness = 4f,
        Rounding = 2f,
        ScreenPosition = new Vector2(0.5f, 0.60f),
        Icon = IconPlace.Hidden,
        NameText = TextPlace.Hidden,
        TimeText = TextPlace.Hidden,
        ShowMoveCue = false,
        ShowQueuedIcon = false,
        FrameThickness = 0f,
        Glow = 0.6f,
        LockTint = 0.85f,
        Colors = new Palette
        {
            Background = Rgb(0x00, 0x00, 0x00, 0.45f),
        },
    };
}
