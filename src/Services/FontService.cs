using System;
using Chant.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace Chant.Services;

/// <summary>
/// Owns the fonts the castbar draws with: the interface face for names (it carries every glyph the
/// game language needs) and the game's own faces from the user's game files. Everything is locked
/// once per frame around drawing.
/// </summary>
public sealed class FontService : IDisposable
{
    /// <summary>Baked large so text stays crisp when a preset scales it up.</summary>
    private const float BakeSizePx = 40f;
    private const float InterfaceBakeSizePx = 24f;

    private static readonly int Count = Enum.GetValues<CastFont>().Length;

    private readonly IFontHandle?[] handles = new IFontHandle?[Count];
    private readonly ILockedImFont?[] locked = new ILockedImFont?[Count];
    private readonly ImFontPtr[] frameFonts = new ImFontPtr[Count];
    private bool inFrame;

    public FontService()
    {
        var atlas = Plugin.PluginInterface.UiBuilder.FontAtlas;
        foreach (var id in Enum.GetValues<CastFont>())
        {
            try
            {
                handles[(int)id] = id switch
                {
                    CastFont.Jupiter => atlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Jupiter, BakeSizePx)),
                    CastFont.TrumpGothic => atlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.TrumpGothic, BakeSizePx)),
                    _ => atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
                    {
                        var font = tk.AddDalamudDefaultFont(InterfaceBakeSizePx);
                        tk.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx = InterfaceBakeSizePx, MergeFont = font });
                    })),
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Could not create font handle for {Font}", id);
            }
        }
    }

    public static string DisplayName(CastFont id) => id switch
    {
        CastFont.Jupiter => "Game serif",
        CastFont.TrumpGothic => "Game condensed",
        _ => "Interface",
    };

    /// <summary>Whether a face covers characters beyond Latin (names in Japanese, Chinese, Korean).</summary>
    public static bool IsLatinOnly(CastFont id) => id is not CastFont.Interface;

    /// <summary>Base pixel size each face is drawn at for a text scale of 1, at 1080p.</summary>
    public static float BaseSize(CastFont id) => id switch
    {
        CastFont.TrumpGothic => 22f,
        CastFont.Jupiter => 18f,
        _ => 17f,
    };

    /// <summary>Locks every ready font for the duration of a frame's drawing.</summary>
    public void BeginFrame()
    {
        if (inFrame) return;
        inFrame = true;

        var fallback = ImGui.GetFont();
        for (var i = 0; i < handles.Length; i++)
        {
            frameFonts[i] = fallback;
            var handle = handles[i];
            if (handle is null || !handle.Available) continue;

            try
            {
                locked[i] = handle.Lock();
                frameFonts[i] = locked[i]!.ImFont;
            }
            catch (Exception)
            {
                locked[i] = null;
            }
        }
    }

    public void EndFrame()
    {
        if (!inFrame) return;
        inFrame = false;

        for (var i = 0; i < locked.Length; i++)
        {
            locked[i]?.Dispose();
            locked[i] = null;
        }
    }

    /// <summary>The font to draw with this frame. Only valid between BeginFrame and EndFrame.</summary>
    public ImFontPtr Get(CastFont id) => frameFonts[(int)id];

    public void Dispose()
    {
        EndFrame();
        foreach (var handle in handles)
            handle?.Dispose();
    }
}
