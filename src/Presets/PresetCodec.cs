using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Chant.Models;
using Newtonsoft.Json;

namespace Chant.Presets;

/// <summary>
/// Share strings: <c>CH1:</c> followed by base64url(deflate(UTF-8 JSON)). The prefix versions the
/// envelope; the preset's own SchemaVersion versions its contents. Imports are validated and
/// clamped, so a malformed or hostile string can at worst produce an odd-looking castbar.
/// </summary>
public static class PresetCodec
{
    private const string Prefix = "CH1:";
    private const int MaxDecodedBytes = 64 * 1024;

    private static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        MaxDepth = 8,
    };

    public static string Encode(Preset preset)
    {
        var json = JsonConvert.SerializeObject(preset, Formatting.None, Settings);
        var raw = Encoding.UTF8.GetBytes(json);

        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);

        return Prefix + Convert.ToBase64String(output.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string text, out Preset preset, out string error)
    {
        preset = new Preset();
        error = string.Empty;

        text = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = "That isn't a Chant preset code (it should start with CH1:).";
            return false;
        }

        try
        {
            var body = text[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            body = body.PadRight(body.Length + (4 - body.Length % 4) % 4, '=');
            var compressed = Convert.FromBase64String(body);

            using var input = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxDecodedBytes)
                {
                    error = "The code is too large to be a preset.";
                    return false;
                }
            }

            var decoded = JsonConvert.DeserializeObject<Preset>(Encoding.UTF8.GetString(output.ToArray()), Settings);
            if (decoded is null)
            {
                error = "The code is empty.";
                return false;
            }

            if (decoded.SchemaVersion > Preset.CurrentSchema)
            {
                error = "This preset was made with a newer Chant. Update the plugin to import it.";
                return false;
            }

            Sanitise(decoded);
            preset = decoded;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Preset import failed");
            error = "The code is damaged or incomplete. Make sure you copied all of it.";
            return false;
        }
    }

    /// <summary>Fresh identity, bounded strings, every number in range.</summary>
    private static void Sanitise(Preset p)
    {
        p.Id = Guid.NewGuid().ToString("N");
        p.IsBuiltIn = false;
        p.Name = Clip(string.IsNullOrWhiteSpace(p.Name) ? "Imported preset" : p.Name, 40);
        p.Description = Clip(p.Description ?? string.Empty, 300);
        p.Author = Clip(p.Author ?? string.Empty, 40);
        p.BasedOn = Clip(p.BasedOn ?? string.Empty, 40);
        p.Colors ??= new Palette();
        p.Clamp();
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
