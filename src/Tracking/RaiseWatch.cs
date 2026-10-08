using System.Collections.Generic;
using Chant.Models;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Chant.Tracking;

/// <summary>A party member raising someone, as the party list already shows.</summary>
public readonly record struct RaiseCast(string Caster, string Target, uint Icon, float Remaining, float Progress, bool OnYourTarget);

/// <summary>
/// Watches your party's cast bars for raises, so two healers don't raise the same person. It reads
/// the same cast information the game shows on the party list.
/// </summary>
public sealed class RaiseWatch
{
    private readonly ActionCatalog catalog;
    private readonly List<RaiseCast> raises = new();

    public IReadOnlyList<RaiseCast> Raises => raises;

    public RaiseWatch(ActionCatalog catalog)
    {
        this.catalog = catalog;
    }

    public void Update()
    {
        raises.Clear();

        var self = Plugin.ObjectTable.LocalPlayer;
        if (self is null) return;

        var target = Plugin.TargetManager.Target;

        foreach (var member in Plugin.PartyList)
        {
            if (member.GameObject is not IBattleChara chara || chara.EntityId == self.EntityId) continue;
            if (!chara.IsCasting || chara.TotalCastTime <= 0f) continue;

            var info = catalog.Get((ActionType)chara.CastActionType, chara.CastActionId);
            if (info.Kind != ActionKind.Raise) continue;

            var targetId = chara.CastTargetObjectId;
            var targetObject = Plugin.ObjectTable.SearchById(targetId);
            var targetName = targetObject?.Name.TextValue ?? "someone";

            raises.Add(new RaiseCast(
                chara.Name.TextValue,
                targetName,
                info.Icon,
                chara.TotalCastTime - chara.CurrentCastTime,
                chara.CurrentCastTime / chara.TotalCastTime,
                target is not null && target.GameObjectId == targetId));
        }
    }

    public void Clear() => raises.Clear();
}
