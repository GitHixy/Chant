using System;
using System.Diagnostics;
using Chant.Models;
using Chant.Rendering;
using Chant.Services;
using Chant.Tracking;
using Chant.UI;
using Chant.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Chant;

/// <summary>
/// Main plugin class for Chant
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Chant";

    private const string CommandName = "/chant";

    public const string RepoUrl = "https://github.com/GitHixy/Chant";
    public const string PatreonUrl = "https://www.patreon.com/GitHixy";
    public const string DiscordUrl = "https://discord.gg/9kjUjWDBkJ";

    // Dalamud Services
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IPluginLog DalamudLog { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IPartyList PartyList { get; private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;

    /// <summary>Plugin-wide logger: writes to Dalamud's log and keeps a copy for the Diagnostics page.</summary>
    public static DiagnosticsLog Log { get; } = new(() => DalamudLog);

    // Plugin Services
    public ConfigurationService ConfigService { get; }
    public FontService Fonts { get; }
    public ActionCatalog Catalog { get; }
    public CastTracker Tracker { get; }
    public RaiseWatch Raises { get; }
    public CastRenderer Renderer { get; }
    public Overlay Overlay { get; }

    // Windows
    public ConfigWindow ConfigWindow { get; }
    public ChangelogWindow ChangelogWindow { get; }

    public Configuration Configuration => ConfigService.Configuration;

    /// <summary>Plays the sample casts on screen. Not saved: off on every load.</summary>
    public bool TestMode { get; set; }

    /// <summary>Shows a drag handle over the castbar. Not saved, and switched off with the settings.</summary>
    public bool Positioning { get; set; }

    private readonly Stopwatch clock = Stopwatch.StartNew();

    public Plugin()
    {
        try
        {
            Log.Info("Chant plugin initializing...");

            ConfigService = new ConfigurationService();
            Fonts = new FontService();
            Catalog = new ActionCatalog();
            Tracker = new CastTracker(ConfigService, Catalog);
            Raises = new RaiseWatch(Catalog);
            Renderer = new CastRenderer(Fonts);
            Overlay = new Overlay(this, Renderer);

            ConfigWindow = new ConfigWindow(this);
            ChangelogWindow = new ChangelogWindow(this);

            Tracker.BecameSafe += OnBecameSafe;

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open Chant settings\n→ /chant test - Play sample casts on screen\n→ /chant move - Drag the castbar into place\n→ /chant toggle - Turn the castbar on or off"
            });

            Framework.Update += OnFrameworkUpdate;
            ClientState.TerritoryChanged += OnTerritoryChanged;

            PluginInterface.UiBuilder.Draw += Draw;
            PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
            PluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;

            ShowChangelogIfUpdated();

            Log.Info("Chant plugin initialized successfully!");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize Chant plugin");
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            Log.Info("Chant plugin disposing...");

            PluginInterface.UiBuilder.Draw -= Draw;
            PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
            PluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;

            Framework.Update -= OnFrameworkUpdate;
            ClientState.TerritoryChanged -= OnTerritoryChanged;
            Tracker.BecameSafe -= OnBecameSafe;

            // The game's cast bar comes back before anything else unloads.
            GameCastBar.SetHidden(false);

            ConfigWindow?.Dispose();
            ChangelogWindow?.Dispose();
            Fonts?.Dispose();
            ConfigService?.Dispose();

            CommandManager.RemoveHandler(CommandName);

            Log.Info("Chant plugin disposed successfully!");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disposing Chant plugin");
        }
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "test":
                TestMode = !TestMode;
                Log.Info("Test casts {State}", TestMode ? "on" : "off");
                break;
            case "move":
                Positioning = !Positioning;
                Log.Info(Positioning ? "Drag the castbar into place, right-click it when done" : "Castbar position set");
                break;
            case "toggle":
                Configuration.Enabled = !Configuration.Enabled;
                ConfigService.Save();
                Log.Info("Castbar {State}", Configuration.Enabled ? "on" : "off");
                break;
            default:
                ConfigWindow.IsOpen = !ConfigWindow.IsOpen;
                break;
        }
    }

    private void OpenConfigUi() => ConfigWindow.IsOpen = true;

    private void ShowChangelogIfUpdated()
    {
        var current = Changelog.CurrentVersion;
        if (ConfigService.IsNewConfiguration)
        {
            Configuration.LastSeenVersion = current;
            ConfigService.Save();
            return;
        }

        if (Configuration.LastSeenVersion != current)
            ChangelogWindow.ShowSince(Configuration.LastSeenVersion);
    }

    private void OnTerritoryChanged(uint territory)
    {
        Tracker.Reset();
        Raises.Clear();
    }

    /// <summary>The game's own chat sound the moment moving becomes safe, for long enough casts.</summary>
    private unsafe void OnBecameSafe(float total)
    {
        var config = Configuration;
        if (!config.SafeChime || total < config.SafeChimeMinCast || Overlay.SuppressedReason is not null) return;
        PlayChime(config.SafeChimeSound);
    }

    public static unsafe void PlayChime(int sound)
    {
        try
        {
            UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(sound, 1, 16));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't play sound {Sound}", sound);
        }
    }

    // ------------------------------------------------------------------
    // Frame
    // ------------------------------------------------------------------

    public double Now => clock.Elapsed.TotalSeconds;

    public bool ReducedMotion => Configuration.ReducedMotion switch
    {
        ReducedMotionMode.On => true,
        ReducedMotionMode.Off => false,
        _ => PluginInterface.UiBuilder.ShouldUseReducedMotion,
    };

    /// <summary>Why the castbar is currently not drawn, or null when it is.</summary>
    public static string? HiddenReason
    {
        get
        {
            if (!ClientState.IsLoggedIn) return "not logged in";
            if (ClientState.IsPvP) return "PvP";
            if (GameGui.GameUiHidden) return "game UI hidden";
            if (ClientState.IsGPosing) return "GPose";
            if (Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51]) return "loading";
            if (Condition[ConditionFlag.OccupiedInCutSceneEvent]) return "cutscene event";
            if (Condition[ConditionFlag.WatchingCutscene]) return "watching cutscene";
            if (Condition[ConditionFlag.WatchingCutscene78]) return "watching cutscene (78)";
            return null;
        }
    }

    /// <summary>Frames whose drawing threw, for the Diagnostics report.</summary>
    public int FrameErrors { get; private set; }
    private int loggedFrameErrors;

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            Tracker.Update(Now, Configuration.Active.Linger);

            if (Configuration.RaiseWatch && HiddenReason is null)
                Raises.Update();
            else
                Raises.Clear();
        }
        catch (Exception ex)
        {
            FrameErrors++;
            if (loggedFrameErrors++ < 5)
                Log.Error(ex, "Error during Chant update");
        }
    }

    private void Draw()
    {
        Fonts.BeginFrame();
        try
        {
            Overlay.Draw(Now, ImGui.GetIO().DeltaTime);

            ConfigWindow.Draw();
            ChangelogWindow.Draw();
            ConfigWindow.SaveIfSettled(Now);
        }
        catch (Exception ex)
        {
            // A per-frame error would flood the log; the first few are enough to diagnose it.
            FrameErrors++;
            if (loggedFrameErrors++ < 5)
                Log.Error(ex, "Error during Chant frame");
        }
        finally
        {
            Fonts.EndFrame();
        }
    }
}
