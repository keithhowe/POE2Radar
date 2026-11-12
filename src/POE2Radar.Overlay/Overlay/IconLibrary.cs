using System.Text;
using System.Text.RegularExpressions;

namespace POE2Radar.Overlay;

/// <summary>One library icon: a name, its SVG viewBox, and one-or-more path <c>d</c> strings
/// (multiple paths render as one shape; the default Alternate fill makes e.g. the Ring a donut).</summary>
public sealed record IconDef(string Name, float VbX, float VbY, float VbW, float VbH, IReadOnlyList<string> Paths)
{
    public string ViewBox => $"{Fmt(VbX)} {Fmt(VbY)} {Fmt(VbW)} {Fmt(VbH)}";
    private static string Fmt(float f) => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The radar icon library. Ships a set of built-in icons (the original six radar shapes reproduced
/// exactly, plus extras), materialized to an <c>icons/</c> folder next to the executable on first run
/// — the same convention <see cref="Config.RadarSettings"/> uses for <c>config/</c>. Any <c>*.svg</c>
/// file in that folder is loaded too; a file whose name matches a built-in overrides it, so users can
/// restyle existing icons or add brand-new ones by dropping in standard single-or-multi-<c>&lt;path&gt;</c>
/// SVGs. A materialized built-in the user has NOT edited is not an override: it is tracked in a small
/// manifest and refreshed when the built-in changes in a newer build (see <see cref="SyncBuiltIns"/>).
/// Lookups are case-insensitive. Loaded once on first access.
/// </summary>
public static class IconLibrary
{
    /// <summary>Resource directory: an "icons" folder next to the executable (sibling of "config").</summary>
    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "icons");

    private static readonly Lazy<(IReadOnlyList<IconDef> Ordered, IReadOnlyDictionary<string, IconDef> Map)> _lib =
        new(Load);

    /// <summary>All icons in display order (built-ins first as authored, then user-only icons by name).</summary>
    public static IReadOnlyList<IconDef> Ordered => _lib.Value.Ordered;

    /// <summary>Case-insensitive name → icon map.</summary>
    public static IReadOnlyDictionary<string, IconDef> Map => _lib.Value.Map;

    /// <summary>True if an icon with this name exists (case-insensitive).</summary>
    public static bool Contains(string? name) => !string.IsNullOrEmpty(name) && Map.ContainsKey(name);

    /// <summary>Canonical (stored-case) name for a case-insensitive lookup, or null if unknown.</summary>
    public static string? Canonical(string? name)
        => !string.IsNullOrEmpty(name) && Map.TryGetValue(name, out var d) ? d.Name : null;

    /// <summary>Record of what the app itself wrote into <see cref="Directory"/>: one
    /// "<c>name&lt;TAB&gt;sha256-of-file-content</c>" line per materialized built-in. It is what tells a
    /// built-in file the user never touched (content still hashes to the recorded value) from one they
    /// edited — only the latter is a user override.</summary>
    private static string ManifestPath => Path.Combine(Directory, "builtin-icons.manifest");

    private static string Hash(string content)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static Dictionary<string, string> ReadManifest()
    {
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(ManifestPath)) return m;
            foreach (var line in File.ReadAllLines(ManifestPath))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var tab = line.IndexOf('\t');
                if (tab <= 0 || tab == line.Length - 1) continue;
                m[line[..tab]] = line[(tab + 1)..].Trim();
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"Icon manifest read failed: {ex.Message}"); }
        return m;
    }

    private static void WriteManifest(Dictionary<string, string> manifest)
    {
        var sb = new StringBuilder();
        sb.Append("# POE2Radar built-in icon manifest (name<TAB>sha256 of the file as the app wrote it). Do not edit:\n");
        sb.Append("# a built-in .svg whose content still matches is kept up to date with the app; edit the .svg\n");
        sb.Append("# itself (or drop in your own) and it becomes YOUR override, which always wins.\n");
        foreach (var kv in manifest.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
        File.WriteAllText(ManifestPath, sb.ToString());
    }

    /// <summary>
    /// Bring the on-disk copies of the built-ins in line with this build and return the names of the
    /// built-ins whose file is UNTOUCHED (so the in-code definition is authoritative for them). A built-in
    /// file is a user override only if its content differs from what the app materialized:
    /// <list type="bullet">
    /// <item>missing and never materialized (first run, or a built-in new in this build) → written;</item>
    /// <item>content == the hash recorded when the app wrote it → untouched; rewritten if the built-in
    /// has changed since, so a newer built-in is never shadowed by its own stale copy;</item>
    /// <item>no record (install from before the manifest existed) and identical to the current built-in
    /// → untouched, recorded;</item>
    /// <item>anything else (edited, or user-dropped under a built-in's name) → left alone; it overrides.</item>
    /// </list>
    /// A built-in file the user DELETED stays deleted (the built-in itself still loads from code).
    /// </summary>
    private static HashSet<string> SyncBuiltIns(List<IconDef> builtins)
    {
        var untouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            System.IO.Directory.CreateDirectory(Directory); // no-op when it exists
            var manifest = ReadManifest();
            var changed = false;
            foreach (var b in builtins)
            {
                try
                {
                    var file = Path.Combine(Directory, b.Name + ".svg");
                    var current = ToSvg(b);
                    var currentHash = Hash(current);
                    var recorded = manifest.TryGetValue(b.Name, out var h) ? h : null;

                    if (!File.Exists(file))
                    {
                        if (recorded is not null) continue;          // we wrote it once; the user removed it
                        File.WriteAllText(file, current);
                        manifest[b.Name] = currentHash; changed = true;
                        untouched.Add(b.Name);
                        continue;
                    }

                    var diskHash = Hash(File.ReadAllText(file));
                    if (diskHash == currentHash)
                    {
                        // Identical to this build's built-in (also the no-manifest legacy case).
                        if (recorded != currentHash) { manifest[b.Name] = currentHash; changed = true; }
                        untouched.Add(b.Name);
                    }
                    else if (recorded is not null && string.Equals(diskHash, recorded, StringComparison.OrdinalIgnoreCase))
                    {
                        // Exactly what an older build wrote, never edited → refresh to the new built-in.
                        File.WriteAllText(file, current);
                        manifest[b.Name] = currentHash; changed = true;
                        untouched.Add(b.Name);
                    }
                    // else: differs from both → the user's own version; it overrides the built-in.
                }
                catch (Exception ex) { Console.Error.WriteLine($"Icon '{b.Name}' materialize failed: {ex.Message}"); }
            }
            if (changed) WriteManifest(manifest);
        }
        catch (Exception ex) { Console.Error.WriteLine($"Icon library materialize failed: {ex.Message}"); }
        return untouched;
    }

    private static (IReadOnlyList<IconDef>, IReadOnlyDictionary<string, IconDef>) Load()
    {
        var builtins = BuiltIns();

        // Materialize / refresh the built-ins on disk so they're discoverable/editable (like config/), and
        // learn which of those files are still the app's own copy rather than a user override.
        var untouched = SyncBuiltIns(builtins);

        // Load any on-disk svgs (these override built-ins of the same name and add new ones). A built-in's
        // untouched materialized copy is NOT an override — skip it so the in-code definition is used.
        var disk = new Dictionary<string, IconDef>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (System.IO.Directory.Exists(Directory))
                foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.svg"))
                {
                    if (untouched.Contains(Path.GetFileNameWithoutExtension(path))) continue;
                    var def = TryParseSvgFile(path);
                    if (def is not null) disk[def.Name] = def;
                }
        }
        catch (Exception ex) { Console.Error.WriteLine($"Icon library load failed: {ex.Message}"); }

        // Merge: built-ins in authored order (disk content wins), then remaining disk-only icons by name.
        var ordered = new List<IconDef>();
        var map = new Dictionary<string, IconDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in builtins)
        {
            var def = disk.TryGetValue(b.Name, out var d) ? d : b;
            disk.Remove(b.Name);
            ordered.Add(def); map[def.Name] = def;
        }
        foreach (var d in disk.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            ordered.Add(d); map[d.Name] = d;
        }
        return (ordered, map);
    }

    private static readonly Regex RxViewBox = new("viewBox\\s*=\\s*\"([^\"]*)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxPathD = new("<path\\b[^>]*?\\bd\\s*=\\s*\"([^\"]*)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>Parse a single .svg file into an <see cref="IconDef"/> (name = filename). Returns null
    /// for files with no &lt;path d="…"&gt; (e.g. icons drawn with &lt;circle&gt;/&lt;polygon&gt;, unsupported).</summary>
    private static IconDef? TryParseSvgFile(string path)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name)) return null;
            var text = File.ReadAllText(path);

            float vx = 0, vy = 0, vw = 24, vh = 24;
            var vb = RxViewBox.Match(text);
            if (vb.Success)
            {
                var p = vb.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 4 &&
                    float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) &&
                    float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) &&
                    float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var c) &&
                    float.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dd) &&
                    c > 0 && dd > 0)
                { vx = a; vy = b; vw = c; vh = dd; }
            }

            var paths = new List<string>();
            foreach (Match m in RxPathD.Matches(text))
                if (!string.IsNullOrWhiteSpace(m.Groups[1].Value)) paths.Add(m.Groups[1].Value.Trim());
            return paths.Count == 0 ? null : new IconDef(name, vx, vy, vw, vh, paths);
        }
        catch { return null; }
    }

    private static string ToSvg(IconDef d)
    {
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"").Append(d.ViewBox).Append("\">\n");
        foreach (var p in d.Paths) sb.Append("  <path d=\"").Append(p).Append("\" />\n");
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    /// <summary>
    /// Built-in icons in a 0 0 24 24 viewBox. The first six (Circle/Square/Triangle/Diamond/Plus/Star)
    /// are authored to reproduce the formerly hardcoded radar geometries pixel-for-pixel after the
    /// renderer's viewBox→unit normalization (Square at 0.9 extent, the rest at full extent), so
    /// existing configs render identically. The rest are new additions.
    /// </summary>
    private static List<IconDef> BuiltIns()
    {
        static IconDef One(string name, string d) => new(name, 0, 0, 24, 24, new[] { d });
        static IconDef Multi(string name, params string[] ds) => new(name, 0, 0, 24, 24, ds);
        return new List<IconDef>
        {
            One("Circle",       "M12 0 C18.627 0 24 5.373 24 12 C24 18.627 18.627 24 12 24 C5.373 24 0 18.627 0 12 C0 5.373 5.373 0 12 0 Z"),
            One("Square",       "M1.2 1.2 L22.8 1.2 L22.8 22.8 L1.2 22.8 Z"),
            One("Triangle",     "M12 0 L22.392 18 L1.608 18 Z"),
            One("Diamond",      "M12 0 L24 12 L12 24 L0 12 Z"),
            One("Plus",         "M7.68 0 L16.32 0 L16.32 7.68 L24 7.68 L24 16.32 L16.32 16.32 L16.32 24 L7.68 24 L7.68 16.32 L0 16.32 L0 7.68 L7.68 7.68 Z"),
            One("Star",         "M12 0 L14.962 7.922 L23.413 8.292 L16.793 13.557 L19.053 21.708 L12 17.04 L4.947 21.708 L7.207 13.557 L0.587 8.292 L9.038 7.922 Z"),
            One("Hexagon",      "M12 0 L22.392 6 L22.392 18 L12 24 L1.608 18 L1.608 6 Z"),
            One("Pentagon",     "M12 0 L23.413 8.292 L19.053 21.708 L4.947 21.708 L0.587 8.292 Z"),
            One("TriangleDown", "M12 24 L1.608 6 L22.392 6 Z"),
            One("ArrowUp",      "M12 1 L22 13 L16 13 L16 23 L8 23 L8 13 L2 13 Z"),
            One("Cross",        "M6 2.5 L12 8.5 L18 2.5 L21.5 6 L15.5 12 L21.5 18 L18 21.5 L12 15.5 L6 21.5 L2.5 18 L8.5 12 L2.5 6 Z"),
            One("Heart",        "M12 21 C12 21 2.5 14.5 2.5 8 C2.5 4.9 4.9 2.5 7.8 2.5 C9.7 2.5 11.2 3.6 12 5 C12.8 3.6 14.3 2.5 16.2 2.5 C19.1 2.5 21.5 4.9 21.5 8 C21.5 14.5 12 21 12 21 Z"),
            One("Droplet",      "M12 1.5 C12 1.5 4.5 11 4.5 16 C4.5 20.1 7.9 23 12 23 C16.1 23 19.5 20.1 19.5 16 C19.5 11 12 1.5 12 1.5 Z"),
            One("Gem",          "M12 1.5 L20.5 9 L12 22.5 L3.5 9 Z"),
            One("Ring",         "M12 1 C18.075 1 23 5.925 23 12 C23 18.075 18.075 23 12 23 C5.925 23 1 18.075 1 12 C1 5.925 5.925 1 12 1 Z M12 6 C15.314 6 18 8.686 18 12 C18 15.314 15.314 18 12 18 C8.686 18 6 15.314 6 12 C6 8.686 8.686 6 12 6 Z"),
            One("Shield",       "M12 1.5 L20.5 4.5 L20.5 11 C20.5 16.3 16.7 21 12 22.5 C7.3 21 3.5 16.3 3.5 11 L3.5 4.5 Z"),
            One("Exclamation",  "M10.4 2.5 L13.6 2.5 L13 14.5 L11 14.5 Z M12 17.5 C13.105 17.5 14 18.395 14 19.5 C14 20.605 13.105 21.5 12 21.5 C10.895 21.5 10 20.605 10 19.5 C10 18.395 10.895 17.5 12 17.5 Z"),

            // ── Curated game-themed glyphs (fill-based, monochrome — tinted by the rule colour). Multi-
            //    path icons rely on the Alternate fill rule (overlapping sub-paths cut holes, like Ring). ──
            Multi("Skull",
                "M12 2 C7.3 2 3.5 5.8 3.5 10.5 C3.5 13.1 4.7 15.4 6.5 16.9 L6.5 19.5 C6.5 20.3 7.2 21 8 21 L9.5 21 L9.5 18.7 L11 18.7 L11 21 L13 21 L13 18.7 L14.5 18.7 L14.5 21 L16 21 C16.8 21 17.5 20.3 17.5 19.5 L17.5 16.9 C19.3 15.4 20.5 13.1 20.5 10.5 C20.5 5.8 16.7 2 12 2 Z",
                "M7.2 10.3 a2.1 2.1 0 1 0 4.2 0 a2.1 2.1 0 1 0 -4.2 0 Z",
                "M12.6 10.3 a2.1 2.1 0 1 0 4.2 0 a2.1 2.1 0 1 0 -4.2 0 Z",
                "M12 12 L13.1 15 L10.9 15 Z"),
            One("Crown",        "M4 8 L7 14 L9.5 6 L12 13.5 L14.5 6 L17 14 L20 8 L18.8 19 L5.2 19 Z"),
            Multi("Person",
                "M9 6 a3 3 0 1 0 6 0 a3 3 0 1 0 -6 0 Z",
                "M4.5 21 C4.5 16 7.8 13.2 12 13.2 C16.2 13.2 19.5 16 19.5 21 Z"),
            One("Chat",         "M4 4 L20 4 C21.1 4 22 4.9 22 6 L22 15 C22 16.1 21.1 17 20 17 L10 17 L5 21 L5 17 L4 17 C2.9 17 2 16.1 2 15 L2 6 C2 4.9 2.9 4 4 4 Z"),
            Multi("Chest",
                "M3 9 C3 6.2 6 4.5 12 4.5 C18 4.5 21 6.2 21 9 L21 19 C21 19.6 20.6 20 20 20 L4 20 C3.4 20 3 19.6 3 19 Z",
                "M3 11 L21 11 L21 12 L3 12 Z",
                "M11.1 13 L12.9 13 L12.9 16 L11.1 16 Z"),
            One("Stairs",       "M3 20 L3 16.5 L7.5 16.5 L7.5 12.5 L12 12.5 L12 8.5 L16.5 8.5 L16.5 4.5 L21 4.5 L21 20 Z"),
            Multi("MapPin",
                "M12 2 C8.1 2 5 5.1 5 9 C5 14.2 12 22 12 22 C12 22 19 14.2 19 9 C19 5.1 15.9 2 12 2 Z",
                "M9.5 9 a2.5 2.5 0 1 0 5 0 a2.5 2.5 0 1 0 -5 0 Z"),
            Multi("Portal",
                "M2 12 a10 10 0 1 0 20 0 a10 10 0 1 0 -20 0 Z M5.5 12 a6.5 6.5 0 1 0 13 0 a6.5 6.5 0 1 0 -13 0 Z",
                "M8.8 12 a3.2 3.2 0 1 0 6.4 0 a3.2 3.2 0 1 0 -6.4 0 Z"),
            One("Flask",        "M9.5 2 L14.5 2 L14.5 3.8 L13.5 3.8 L13.5 8 C13.5 8.6 13.7 9.2 14.1 9.7 L17.2 14.2 C18.3 15.8 17.1 18 15.2 18 L8.8 18 C6.9 18 5.7 15.8 6.8 14.2 L9.9 9.7 C10.3 9.2 10.5 8.6 10.5 8 L10.5 3.8 L9.5 3.8 Z"),
            Multi("Flag",
                "M5 2 L6.5 2 L6.5 22 L5 22 Z",
                "M6.5 3 L18 3 L15 7 L18 11 L6.5 11 Z"),
            Multi("Eye",
                "M2 12 C5 6 9 4 12 4 C15 4 19 6 22 12 C19 18 15 20 12 20 C9 20 5 18 2 12 Z",
                "M9 12 a3 3 0 1 0 6 0 a3 3 0 1 0 -6 0 Z"),
            Multi("Coin",
                "M2 12 a10 10 0 1 0 20 0 a10 10 0 1 0 -20 0 Z M5 12 a7 7 0 1 0 14 0 a7 7 0 1 0 -14 0 Z",
                "M9 12 a3 3 0 1 0 6 0 a3 3 0 1 0 -6 0 Z"),
            Multi("Sword",
                "M12 1.5 L13 3.5 L13 14 L11 14 L11 3.5 Z",
                "M7.5 14 L16.5 14 L16.5 15.8 L7.5 15.8 Z",
                "M11 15.8 L13 15.8 L13 20 L11 20 Z",
                "M10.3 20 L13.7 20 L13.7 21.8 L10.3 21.8 Z"),
            // Monster rarity ramp: Fang (magic) → Claw (rare) → Skull (unique). Read as escalating threat.
            One("Fang",         "M7 3 L17 3 C17 3 16 9 14 14 L12 21 L10 14 C8 9 7 3 7 3 Z"),
            Multi("Claw",
                "M4.6 2 C7.8 7 7.8 15 6.6 21 C3.2 16 2.8 7 4.6 2 Z",
                "M11.1 1.4 C14.3 6.4 14.3 15 13.1 21 C9.7 16 9.3 6.4 11.1 1.4 Z",
                "M17.6 2 C20.8 7 20.8 15 19.6 21 C16.2 16 15.8 7 17.6 2 Z"),
        };
    }
}
