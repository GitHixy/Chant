using System;
using System.Collections.Generic;
using System.Linq;

namespace Chant.UI;

/// <summary>What a single release note is about, which drives its colour and tag.</summary>
public enum NoteKind
{
    Added,
    Changed,
    Improved,
    Fixed,
    Removed,
}

/// <summary>One line in a release's notes.</summary>
public readonly record struct Note(NoteKind Kind, string Text);

/// <summary>One published version and everything that went into it.</summary>
public readonly record struct Release(string Version, string Headline, Note[] Notes);

/// <summary>
/// The release notes shown after an update. Newest first - add a new entry at the top
/// whenever the assembly version in Chant.csproj changes.
/// </summary>
public static class Changelog
{
    public static readonly Release[] Releases =
    [
        new("0.1.0", "Welcome to Chant",
        [
            new(NoteKind.Added, "Chant draws your cast bar in its place, built to be read at a glance: it stays red while moving "
                                + "would cancel your cast and turns the moment it's safe to go."),
            new(NoteKind.Added, "A get-ready phase just before the slidecast window, and a warning if you start moving too early."),
            new(NoteKind.Added, "The queue window marked on the bar, and the icon of the action you've already queued next to it."),
            new(NoteKind.Added, "Your spell's icon and name, coloured by what it does: damage, healing, raises, support and "
                                + "everything else."),
            new(NoteKind.Added, "Seven presets in different shapes - Aether, Sigil, Halo, Pillar, Crescent, Runic and Whisper: "
                                + "horizontal and vertical bars, rings and arcs, on screen or following your character."),
            new(NoteKind.Added, "Every size, colour, piece of text and animation can be tuned with a live preview. Each preset remembers "
                                + "your changes and position when you switch, and any of them can be saved and shared as a short code."),
            new(NoteKind.Added, "Drag the castbar into place right on the screen (Move castbar, or /chant move)."),
            new(NoteKind.Added, "When a cast is cancelled, Chant says how early you moved. Your session's slidecasts are counted "
                                + "in Settings, along with a suggestion when your slidecast window looks too wide."),
            new(NoteKind.Added, "The global cooldown on the same shape after instant actions, so the queue window works for every job."),
            new(NoteKind.Added, "Party raise watch (Settings → General): see when another healer is already raising someone, and whom. "
                                + "Keep it under the castbar or drag it anywhere on screen."),
            new(NoteKind.Added, "An optional chime the moment it's safe to move, for long casts."),
            new(NoteKind.Added, "A Diagnostics page with a live log and Copy report, plus buttons to report a bug or suggest a "
                                + "feature on GitHub."),
        ]),
    ];

    /// <summary>The running assembly's version, formatted the way the notes list them.</summary>
    public static string CurrentVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>The newest release, or null when the list is empty.</summary>
    public static Release? Latest => Releases.Length > 0 ? Releases[0] : null;

    /// <summary>
    /// Everything published since <paramref name="sinceVersion"/>. An unknown or missing
    /// version yields just the newest release, so an upgrade never dumps the whole history.
    /// </summary>
    public static IReadOnlyList<Release> Since(string sinceVersion)
    {
        if (!Version.TryParse(sinceVersion, out var seen))
            return Latest.HasValue ? [Latest.Value] : [];

        return Releases
            .Where(r => Version.TryParse(r.Version, out var v) && v > seen)
            .ToList();
    }

    public static string KindLabel(NoteKind kind) => kind switch
    {
        NoteKind.Added => "NEW",
        NoteKind.Changed => "CHANGED",
        NoteKind.Improved => "IMPROVED",
        NoteKind.Fixed => "FIXED",
        NoteKind.Removed => "REMOVED",
        _ => "NOTE",
    };

    public static System.Numerics.Vector4 KindColor(NoteKind kind) => kind switch
    {
        NoteKind.Added => Theme.Good,
        NoteKind.Changed => Theme.Crystal,
        NoteKind.Improved => Theme.Gold,
        NoteKind.Fixed => Theme.Warn,
        NoteKind.Removed => Theme.Bad,
        _ => Theme.TextMuted,
    };
}
