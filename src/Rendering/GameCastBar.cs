using FFXIVClientStructs.FFXIV.Client.UI;

namespace Chant.Rendering;

/// <summary>
/// Hides the game's own cast bar (the _CastBar addon) while Chant draws in its place. Only whether
/// its root node is drawn is touched, and only what Chant hid is ever shown again.
/// </summary>
public static class GameCastBar
{
    private const string Addon = "_CastBar";

    /// <summary>Whether the root node was hidden by Chant, so only that is ever undone.</summary>
    private static bool hiddenByUs;

    public static bool HiddenByUs => hiddenByUs;

    /// <summary>
    /// Keeps the game's cast bar hidden or shown. Called every frame, since the game can rebuild the
    /// addon (HUD layout changes) and show it again.
    /// </summary>
    public static unsafe void SetHidden(bool hide)
    {
        var manager = RaptureAtkUnitManager.Instance();
        var unit = manager is null ? null : manager->GetAddonByName(Addon);
        if (unit is null || unit->RootNode is null)
        {
            // Gone with whatever we did to it; a rebuilt one starts visible.
            if (!hide) hiddenByUs = false;
            return;
        }

        var root = unit->RootNode;
        if (hide)
        {
            if (!root->IsVisible()) return;
            root->ToggleVisibility(false);
            hiddenByUs = true;
        }
        else if (hiddenByUs)
        {
            root->ToggleVisibility(true);
            hiddenByUs = false;
        }
    }
}
