using System.Text.Json;

namespace POE2Radar.Core.Game;

/// <summary>
/// Runtime overrides for the <see cref="Poe2"/> offset table, loaded once from
/// <c>config/offsets.json</c> next to the executable.
///
/// <para><b>Why this exists.</b> PoE2 struct offsets drift with every game patch. When they were
/// <c>const</c> the value was inlined into the IL of every assembly that read it, so correcting one
/// meant editing source, rebuilding, re-tagging and shipping a fresh self-contained exe to users.
/// As <c>static readonly</c> fields seeded through this class, a patch fix becomes a DATA change:
/// drop a corrected <c>offsets.json</c> next to the exe (or ship it as a repo file users pull) and
/// restart — no rebuild, no release.</para>
///
/// <para>This solves DELIVERY of a corrected value, not DISCOVERY of it. Finding the new value is
/// still the Research probes' job (<c>--areascan</c>, <c>--chaindbg</c>, <c>--uitoggle</c>, …).</para>
///
/// <para><b>Fail-safe by construction.</b> A missing file, malformed JSON, or an unparsable entry
/// leaves the compiled-in defaults in place and records the reason in <see cref="LoadError"/>; it
/// never throws. Static-initialisation order is not a hazard because the file is located from
/// <see cref="AppContext.BaseDirectory"/> on first use rather than via an Initialize() call some
/// caller might forget to make before the first offset read.</para>
///
/// <para>File shape — one object per offset class, values as hex strings or numbers:</para>
/// <code>
/// {
///   "_comment": "PoE2 build 2026-09-04",
///   "AreaInstance": { "LocalPlayer": "0x5D0", "AwakeEntities": "0x6F0" },
///   "InGameState":  { "Camera": "0x378" }
/// }
/// </code>
/// </summary>
public static class OffsetOverrides
{
    private static readonly Dictionary<string, double> Values = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> AppliedList = new();

    /// <summary>Path consulted for overrides (whether or not it existed).</summary>
    public static string? SourcePath { get; private set; }

    /// <summary>True when an overrides file was found and parsed.</summary>
    public static bool Loaded { get; private set; }

    /// <summary>Why loading failed, or null. A parse failure is non-fatal: defaults are kept.</summary>
    public static string? LoadError { get; private set; }

    /// <summary>Human-readable "Class.Field 0xOLD -> 0xNEW" lines for every override that actually
    /// CHANGED a value. An entry equal to the built-in default is not listed — it is a no-op, and
    /// listing it would make a stale file look like it is doing something.</summary>
    public static IReadOnlyList<string> Applied => AppliedList;

    static OffsetOverrides() => Load();

    private static void Load()
    {
        try
        {
            // Env var first so a probe run can point at an experimental table without disturbing
            // the installed config.
            var path = Environment.GetEnvironmentVariable("POE2RADAR_OFFSETS");
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(AppContext.BaseDirectory, "config", "offsets.json");
            SourcePath = path;

            if (!File.Exists(path)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            foreach (var section in doc.RootElement.EnumerateObject())
            {
                if (section.Name.StartsWith('_')) continue;                 // "_comment" and friends
                if (section.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var field in section.Value.EnumerateObject())
                {
                    if (field.Name.StartsWith('_')) continue;
                    if (TryParse(field.Value, out var v)) Values[$"{section.Name}.{field.Name}"] = v;
                }
            }
            Loaded = true;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
    }

    /// <summary>Accepts a JSON number, or a string holding decimal or "0x"-prefixed hex (the form
    /// offsets are written in everywhere else, so the file reads like the source table).</summary>
    private static bool TryParse(JsonElement el, out double value)
    {
        value = 0;
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                return el.TryGetDouble(out value);
            case JsonValueKind.String:
                var s = el.GetString()?.Trim();
                if (string.IsNullOrEmpty(s)) return false;
                var neg = s.StartsWith('-');
                if (neg) s = s[1..].TrimStart();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (!long.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber,
                                       System.Globalization.CultureInfo.InvariantCulture, out var hex)) return false;
                    value = neg ? -hex : hex;
                    return true;
                }
                if (!double.TryParse(s, System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture, out value)) return false;
                if (neg) value = -value;
                return true;
            default:
                return false;
        }
    }

    private static bool TryLookup(string key, out double v) => Values.TryGetValue(key, out v);

    private static void Note(string key, string oldText, string newText) =>
        AppliedList.Add($"{key} {oldText} -> {newText}");

    /// <summary>Offset lookup: the override for <paramref name="key"/> ("Class.Field") or
    /// <paramref name="fallback"/>. Every field in <see cref="Poe2"/> is seeded through here.</summary>
    public static int Get(string key, int fallback)
    {
        if (!TryLookup(key, out var v)) return fallback;
        var i = (int)v;
        if (i == fallback) return fallback;
        Note(key, $"0x{fallback:X}", $"0x{i:X}");
        return i;
    }

    public static uint Get(string key, uint fallback)
    {
        if (!TryLookup(key, out var v) || v < 0) return fallback;
        var u = (uint)v;
        if (u == fallback) return fallback;
        Note(key, $"0x{fallback:X}", $"0x{u:X}");
        return u;
    }

    public static float Get(string key, float fallback)
    {
        if (!TryLookup(key, out var v)) return fallback;
        var f = (float)v;
        if (Math.Abs(f - fallback) < float.Epsilon) return fallback;
        Note(key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture),
                  f.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return f;
    }

    /// <summary>One-line startup summary for the console/log.</summary>
    public static string Describe()
    {
        if (LoadError != null) return $"Offsets: override file FAILED to load ({LoadError}) — using built-in defaults.";
        if (!Loaded) return $"Offsets: built-in defaults (no {SourcePath}).";
        return AppliedList.Count == 0
            ? $"Offsets: loaded {SourcePath} — no value differed from the built-in defaults."
            : $"Offsets: {AppliedList.Count} override(s) from {SourcePath} — {string.Join(", ", AppliedList)}";
    }
}
