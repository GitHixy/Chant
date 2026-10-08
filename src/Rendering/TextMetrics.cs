using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Chant.Rendering;

/// <summary>Measures single-line text in any font at any size, from the font's own glyph advances.</summary>
public static class TextMetrics
{
    public static unsafe Vector2 Measure(ImFontPtr font, float size, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return new Vector2(0f, size);

        var k = size / Math.Max(font.FontSize, 1f);
        var width = 0f;
        foreach (var c in text)
        {
            var glyph = font.FindGlyph(c);
            if (glyph != null) width += glyph->AdvanceX * k;
        }

        return new Vector2(width, size);
    }
}
