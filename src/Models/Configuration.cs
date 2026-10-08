using System;
using System.Collections.Generic;
using System.Numerics;

namespace Chant.Models;

public enum RaiseWatchPlacement
{
    /// <summary>Right under the castbar, moving with it.</summary>
    UnderCastbar,
    /// <summary>A fixed spot on screen of your choosing.</summary>
    Free,
}

public enum ReducedMotionMode
{
    /// <summary>Follow Dalamud's reduced motion setting.</summary>
    Auto,
    On,
    Off,
}

/// <summary>
/// Configuration settings for the Chant plugin
/// </summary>
[Serializable]
public class Configuration
{
    public int Version { get; set; } = 1;

    /// <summary>Master switch.</summary>
    public bool Enabled { get; set; } = true;

    // Look
    /// <summary>The castbar in use: the user's own copy of a preset, edited freely.</summary>
    public Preset Active { get; set; } = Presets.BuiltInPresets.Default();
    public List<Preset> UserPresets { get; set; } = new();
    /// <summary>
    /// Your version of every preset you've used, by preset name: switching away keeps your changes and
    /// position, and switching back brings them back.
    /// </summary>
    public Dictionary<string, Preset> PresetEdits { get; set; } = new();
    public float Opacity { get; set; } = 1f;
    public ReducedMotionMode ReducedMotion { get; set; } = ReducedMotionMode.Auto;

    // Timing
    /// <summary>
    /// How long before the end of a cast you can move without cancelling it. The game commits the
    /// cast a little before the bar ends; half a second is the usual figure, less on a slow connection.
    /// </summary>
    public float SlideWindow { get; set; } = 0.5f;
    /// <summary>How long before the slidecast window the bar warns you to get ready.</summary>
    public float ReadyLead { get; set; } = 0.35f;
    /// <summary>The game accepts a queued action this long before you're free to act.</summary>
    public float QueueWindow { get; set; } = 0.5f;

    // Behaviour
    /// <summary>Hide the game's own cast bar while Chant is drawing.</summary>
    public bool HideGameCastBar { get; set; } = true;
    /// <summary>Show the global cooldown on the same shape after instant actions.</summary>
    public bool ShowGcd { get; set; } = true;
    /// <summary>Warn when you move while it isn't safe yet.</summary>
    public bool MoveWarning { get; set; } = true;
    /// <summary>After a cancelled cast, say how early you moved.</summary>
    public bool InterruptCoach { get; set; } = true;
    /// <summary>Hide while not in combat (test casts and settings preview still show).</summary>
    public bool OnlyInCombat { get; set; }
    /// <summary>Hide mount, item, teleport and other non-combat casts.</summary>
    public bool HideUtilityCasts { get; set; }

    // Sound
    /// <summary>Play a game sound the moment it's safe to move.</summary>
    public bool SafeChime { get; set; }
    /// <summary>Which of the game's chat sounds, 1 to 16 (the &lt;se.N&gt; sounds).</summary>
    public int SafeChimeSound { get; set; } = 6;
    /// <summary>Only chime for casts at least this long, so short casts stay quiet.</summary>
    public float SafeChimeMinCast { get; set; } = 2.4f;

    // Party
    /// <summary>Show when party members are raising someone, and whom.</summary>
    public bool RaiseWatch { get; set; }
    public RaiseWatchPlacement RaiseWatchPlacement { get; set; } = RaiseWatchPlacement.UnderCastbar;
    /// <summary>Top centre of the raise watch as a fraction of the screen, for <see cref="RaiseWatchPlacement.Free"/>.</summary>
    public Vector2 RaiseWatchPosition { get; set; } = new(0.5f, 0.80f);

    // Diagnostics
    /// <summary>Write every cast, its outcome and its classification to the Diagnostics log.</summary>
    public bool LogEvents { get; set; }

    // Release notes
    /// <summary>Last plugin version whose changelog the user dismissed.</summary>
    public string LastSeenVersion { get; set; } = string.Empty;
}
