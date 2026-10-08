using System;
using System.Collections.Generic;
using Chant.Models;
using ClientLanguage = Dalamud.Game.ClientLanguage;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaAction = Lumina.Excel.Sheets.Action;
using Lumina.Excel.Sheets;

namespace Chant.Tracking;

/// <summary>A cast's name, icon and colour group, and why it got that group (for Diagnostics).</summary>
public readonly record struct ActionInfo(string Name, uint Icon, ActionKind Kind, string Why);

/// <summary>
/// Names, icons and kinds of everything that can show a cast bar, read from the game's sheets once
/// per action and remembered.
/// </summary>
/// <remarks>
/// The sheets don't say "this is a heal", so spells are sorted by who they can target and by the
/// English tooltip (which reads the same whatever language the game runs in): anything aimed at an
/// enemy is damage, a tooltip that resurrects is a raise, one that restores HP is a heal, and the
/// rest of the friendly spells are support. Everything that isn't a combat action is utility.
/// </remarks>
public sealed class ActionCatalog
{
    /// <summary>Raises, by id, in case a tooltip ever stops saying so.</summary>
    private static readonly HashSet<uint> RaiseIds =
    [
        125,   // Raise
        173,   // Resurrection
        3603,  // Ascend
        7523,  // Verraise
        24287, // Egeiro
        18317, // Angel Whisper
    ];

    private readonly Dictionary<(ActionType Type, uint Id), ActionInfo> cache = new();

    public ActionInfo Get(ActionType type, uint id)
    {
        if (cache.TryGetValue((type, id), out var info))
            return info;

        try
        {
            info = Lookup(type, id);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Couldn't read action {Type} {Id}", type, id);
            info = new ActionInfo("Casting", 0, ActionKind.Utility, "lookup failed");
        }

        cache[(type, id)] = info;
        return info;
    }

    private static ActionInfo Lookup(ActionType type, uint id)
    {
        var data = Plugin.DataManager;
        switch (type)
        {
            case ActionType.Action:
            case ActionType.PvPAction:
            {
                if (data.GetExcelSheet<LuminaAction>().GetRowOrDefault(id) is not { } row)
                    break;
                var (kind, why) = Classify(id, row);
                return new ActionInfo(Text(row.Name, "Casting"), row.Icon, kind, why);
            }
            case ActionType.Item:
            {
                // High quality items are offset by a million, collectables by half a million.
                var itemId = id % 500_000;
                if (data.GetExcelSheet<Item>().GetRowOrDefault(itemId) is { } row)
                    return new ActionInfo(Text(row.Name, "Item"), row.Icon, ActionKind.Utility, "item");
                break;
            }
            case ActionType.Mount:
                if (data.GetExcelSheet<Mount>().GetRowOrDefault(id) is { } mount)
                    return new ActionInfo(Capitalise(Text(mount.Singular, "Mount")), mount.Icon, ActionKind.Utility, "mount");
                break;
            case ActionType.GeneralAction:
                if (data.GetExcelSheet<GeneralAction>().GetRowOrDefault(id) is { } general)
                    return new ActionInfo(Text(general.Name, "Action"), (uint)Math.Max(0, general.Icon), ActionKind.Utility, "general action");
                break;
            case ActionType.EventAction:
                if (data.GetExcelSheet<EventAction>().GetRowOrDefault(id) is { } ev)
                    return new ActionInfo(Text(ev.Name, "Interacting"), ev.Icon, ActionKind.Utility, "event action");
                break;
            case ActionType.CraftAction:
                if (data.GetExcelSheet<CraftAction>().GetRowOrDefault(id) is { } craft)
                    return new ActionInfo(Text(craft.Name, "Crafting"), craft.Icon, ActionKind.Utility, "craft action");
                break;
        }

        return new ActionInfo("Casting", 0, ActionKind.Utility, $"no sheet row ({type})");
    }

    private static (ActionKind Kind, string Why) Classify(uint id, LuminaAction row)
    {
        if (RaiseIds.Contains(id))
            return (ActionKind.Raise, "known raise");

        // Teleport, Return, gathering and quest actions are actions too, but not combat ones:
        // only spells, weaponskills, abilities and limit breaks get a combat colour.
        var category = row.ActionCategory.RowId;
        if (category is not (2 or 3 or 4 or 9) && !row.CanTargetHostile)
            return (ActionKind.Utility, $"category {category}");

        var tooltip = string.Empty;
        if (Plugin.DataManager.GetExcelSheet<ActionTransient>(ClientLanguage.English).GetRowOrDefault(id) is { } transient)
            tooltip = Text(transient.Description, string.Empty);

        if (tooltip.Contains("Resurrects", StringComparison.OrdinalIgnoreCase))
            return (ActionKind.Raise, "tooltip resurrects");

        if (row.CanTargetHostile)
            return (ActionKind.Damage, "targets enemies");

        if (tooltip.Contains("Cure Potency", StringComparison.OrdinalIgnoreCase)
            || tooltip.Contains("Healing Potency", StringComparison.OrdinalIgnoreCase)
            || tooltip.Contains("Restores", StringComparison.OrdinalIgnoreCase) && tooltip.Contains("HP", StringComparison.Ordinal))
            return (ActionKind.Heal, "tooltip heals");

        return (ActionKind.Support, "friendly, not a heal");
    }

    private static string Text(Lumina.Text.ReadOnly.ReadOnlySeString value, string fallback)
    {
        var text = value.ExtractText();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
