using System.Reflection;
using System.Text.Json;

namespace POE2Radar.Core.Game;

/// <summary>
/// Curated landmark labels keyed by area code -> terrain tile path -> human-friendly label
/// (boss arena + reward, waypoint, area transition destination, etc.). Loaded once from the
/// embedded <c>CustomLandmarks.json</c>. Lookup is area-specific first, then the global "*" bucket.
/// </summary>
public static class CustomLandmarkData
{
    // Built once, published only when complete (Lazy = thread-safe): the world thread's landmark scan and
    // the API thread's dashboard listing both call Load(), and must never see a half-filled table.
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Data = new(Build);

    public static IReadOnlyDictionary<string, Dictionary<string, string>> Load() => Data.Value;

    private static Dictionary<string, Dictionary<string, string>> Build()
    {
        var data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.Contains("CustomLandmarks"));
            if (resName == null) return data;

            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null) return data;

            var doc = JsonDocument.Parse(stream);
            foreach (var areaProp in doc.RootElement.EnumerateObject())
            {
                var areaCode = areaProp.Name;
                var tiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tileProp in areaProp.Value.EnumerateObject())
                {
                    var tgtPath = tileProp.Name;
                    var colonIdx = tgtPath.IndexOf(':');
                    var cleanPath = colonIdx >= 0 ? tgtPath[..colonIdx] : tgtPath;
                    cleanPath = cleanPath.Replace(".tdtx", ".tdt");
                    tiles[cleanPath] = tileProp.Value.GetString() ?? tileProp.Name;
                }
                data[areaCode] = tiles;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load custom landmarks: {ex.Message}");
        }
        return data;
    }

    public static string? TryMatch(string areaCode, string tilePath)
    {
        var data = Load();

        if (data.TryGetValue(areaCode, out var areaMap))
        {
            foreach (var (pattern, label) in areaMap)
                if (tilePath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return label;
        }

        if (data.TryGetValue("*", out var globalMap))
        {
            foreach (var (pattern, label) in globalMap)
                if (tilePath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return label;
        }

        return null;
    }
}
