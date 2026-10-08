using Dalamud.Interface;

namespace Chant.Models;

/// <summary>What a cast does, which picks its colour.</summary>
public enum ActionKind
{
    Damage,
    Heal,
    Raise,
    Support,
    Utility,
}

public static class ActionKindInfo
{
    public static readonly ActionKind[] All = [ActionKind.Damage, ActionKind.Heal, ActionKind.Raise, ActionKind.Support, ActionKind.Utility];

    public static string Name(ActionKind kind) => kind switch
    {
        ActionKind.Damage => "Damage",
        ActionKind.Heal => "Healing",
        ActionKind.Raise => "Raise",
        ActionKind.Support => "Support",
        _ => "Everything else",
    };

    public static string Blurb(ActionKind kind) => kind switch
    {
        ActionKind.Damage => "Spells cast at an enemy.",
        ActionKind.Heal => "Spells that restore HP.",
        ActionKind.Raise => "Raise, Resurrection, Ascend, Egeiro, Verraise and the like.",
        ActionKind.Support => "Other spells on you or your party: summons, buffs, shields.",
        _ => "Mounts, items, teleports, crafting, gathering and quest actions.",
    };

    public static FontAwesomeIcon Icon(ActionKind kind) => kind switch
    {
        ActionKind.Damage => FontAwesomeIcon.Fire,
        ActionKind.Heal => FontAwesomeIcon.Heart,
        ActionKind.Raise => FontAwesomeIcon.Feather,
        ActionKind.Support => FontAwesomeIcon.ShieldAlt,
        _ => FontAwesomeIcon.Cube,
    };
}
