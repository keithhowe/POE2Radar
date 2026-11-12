using System.Text.Json;

namespace POE2Radar.Overlay.Web;

/// <summary>One user landmark rule: surface any terrain tile whose path contains <see cref="Pattern"/>
/// (case-insensitive) as a navigable landmark, optionally giving it a friendly <see cref="Label"/>
/// (blank = use the path-derived name). <see cref="Enabled"/> toggles it without deleting.</summary>
public sealed record LandmarkPattern(string Pattern, string Label = "", bool Enabled = true);

/// <summary>
/// User-managed, dashboard-editable list of tile-path patterns to surface as landmarks — on top of the
/// built-in keyword filter and the embedded curated list. Tiles are the position-independent layer
/// (scanned per area, visible anywhere on the map), so this is where a user names the tile features
/// they care about. Persisted to <c>config/landmark_patterns.json</c>.
///
/// <para><see cref="Match"/> is consulted by the Core landmark scan (via a delegate) once per distinct
/// tile path per area; it reads an immutable snapshot lock-free. Mutations rebuild the snapshot under a
/// lock and bump <see cref="Generation"/> so the tick loop can invalidate the per-area scan cache.</para>
/// </summary>
public sealed class LandmarkPatterns
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly Dictionary<string, LandmarkPattern> _entries = new(StringComparer.OrdinalIgnoreCase);
    private volatile LandmarkPattern[] _enabledSnapshot = Array.Empty<LandmarkPattern>();
    private volatile int _generation;
    private static readonly JsonSerializerOptions Json =
        new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public LandmarkPatterns(string filePath)
    {
        _filePath = filePath;
        Load();
        Rebuild();
    }

    /// <summary>All rules (snapshot copy; safe to enumerate off-thread).</summary>
    public IReadOnlyList<LandmarkPattern> All { get { lock (_gate) return _entries.Values.ToArray(); } }

    /// <summary>Bumped on every mutation; the tick loop watches this to invalidate the landmark cache.</summary>
    public int Generation => _generation;

    /// <summary>
    /// The label for the first ENABLED rule whose pattern is contained in <paramref name="tilePath"/>,
    /// or null if none match. The label may be empty (surface the tile, keep its derived name). Lock-free.
    /// </summary>
    public string? Match(string tilePath)
    {
        if (string.IsNullOrEmpty(tilePath)) return null;
        var snap = _enabledSnapshot; // volatile read of an immutable array
        foreach (var e in snap)
            if (tilePath.Contains(e.Pattern, StringComparison.OrdinalIgnoreCase)) return e.Label ?? "";
        return null;
    }

    public void Add(string pattern, string label = "")
    {
        if (string.IsNullOrWhiteSpace(pattern)) return;
        lock (_gate)
        {
            _entries[pattern.Trim()] = new LandmarkPattern(pattern.Trim(), label?.Trim() ?? "", true);
            Rebuild(); Save();
        }
    }

    public void Update(string pattern, string? label = null, bool? enabled = null)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(pattern, out var e)) return;
            _entries[pattern] = e with { Label = label ?? e.Label, Enabled = enabled ?? e.Enabled };
            Rebuild(); Save();
        }
    }

    public void Remove(string pattern)
    {
        lock (_gate)
        {
            if (!_entries.Remove(pattern)) return;
            Rebuild(); Save();
        }
    }

    /// <summary>Rebuild the immutable enabled-only snapshot + bump the generation. Call under <see cref="_gate"/>.</summary>
    private void Rebuild()
    {
        _enabledSnapshot = _entries.Values.Where(e => e.Enabled).ToArray();
        _generation++;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var list = JsonSerializer.Deserialize<List<LandmarkPattern>>(File.ReadAllText(_filePath), Json);
            if (list == null) return;
            foreach (var e in list)
                if (!string.IsNullOrWhiteSpace(e.Pattern)) _entries[e.Pattern] = e;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Landmark patterns load failed: {ex.Message}"); }
    }

    private void Save()
    {
        try
        {
            Config.AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(_entries.Values.ToList(), Json));
        }
        catch (Exception ex) { Console.Error.WriteLine($"Landmark patterns save failed: {ex.Message}"); }
    }
}
