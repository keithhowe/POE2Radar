using System.Text.Json;
using System.Text.RegularExpressions;

namespace POE2Radar.Overlay.Web;

/// <summary>
/// User-managed cull list: entity metadata substrings (or <c>*</c>/<c>?</c> globs) whose entities are
/// hidden from the radar, the published entity list, and nav-target building — a noise filter for
/// things like persistent area-effect markers. Persisted to <c>config/hidden_entities.json</c>.
///
/// <para>Thread-safety: <see cref="IsHidden"/> runs on the tick thread for every entity each world
/// tick, while mutations come from the HTTP API thread. We keep a precompiled immutable snapshot in
/// a volatile field so the hot path is lock-free; mutations rebuild the snapshot under a lock and
/// swap it in. (The fork accessed a shared HashSet without guarding it; this avoids that race.)</para>
/// </summary>
public sealed class HiddenEntities
{
    /// <summary>A compiled pattern: a literal substring, or a glob lowered to a regex.</summary>
    private readonly record struct Pattern(string Raw, Regex? Glob);

    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly SortedSet<string> _patterns = new(StringComparer.OrdinalIgnoreCase); // owned under _gate
    private volatile Pattern[] _compiled = Array.Empty<Pattern>();                        // immutable snapshot
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public HiddenEntities(string filePath)
    {
        _filePath = filePath;
        // Seed the default ONLY on a true first run (no file yet). An existing file that holds an empty
        // list is the user's choice ("nothing hidden") and must survive a restart — re-seeding whenever
        // the list was empty made it impossible to keep it empty.
        var firstRun = !File.Exists(_filePath);
        Load();
        if (firstRun) _patterns.Add("AbyssCrack"); // sensible default, persisted below
        Rebuild();
        if (firstRun) Save();
    }

    /// <summary>Current patterns (snapshot copy; safe to enumerate off-thread).</summary>
    public IReadOnlyList<string> All { get { lock (_gate) return _patterns.ToArray(); } }

    /// <summary>Number of active patterns. Lock-free (reads the compiled snapshot) for the hot path.</summary>
    public int Count => _compiled.Length;

    /// <summary>True if <paramref name="text"/> matches any hide pattern. Lock-free hot path.</summary>
    public bool IsHidden(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var compiled = _compiled; // volatile read of an immutable array
        foreach (var p in compiled)
        {
            if (p.Glob is { } rx) { if (rx.IsMatch(text)) return true; }
            else if (text.Contains(p.Raw, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public bool Add(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        pattern = pattern.Trim();
        lock (_gate)
        {
            if (!_patterns.Add(pattern)) return false;
            Rebuild(); Save();
        }
        return true;
    }

    public bool Remove(string pattern)
    {
        lock (_gate)
        {
            if (!_patterns.Remove(pattern)) return false;
            Rebuild(); Save();
        }
        return true;
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_patterns.Count == 0) return;
            _patterns.Clear();
            Rebuild(); Save();
        }
    }

    private static bool IsGlob(string pattern) => pattern.Contains('*') || pattern.Contains('?');

    private static Regex CompileGlob(string pattern)
    {
        var regexStr = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return new Regex(regexStr, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    /// <summary>Rebuild the immutable compiled snapshot from <see cref="_patterns"/>. Call under <see cref="_gate"/>.</summary>
    private void Rebuild()
        => _compiled = _patterns.Select(p => new Pattern(p, IsGlob(p) ? CompileGlob(p) : null)).ToArray();

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_filePath), Json);
            if (list == null) return;
            foreach (var p in list)
                if (!string.IsNullOrWhiteSpace(p)) _patterns.Add(p.Trim());
        }
        catch (Exception ex) { Console.Error.WriteLine($"Hidden entities load failed: {ex.Message}"); }
    }

    private void Save()
    {
        try
        {
            Config.AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(_patterns.ToList(), Json));
        }
        catch (Exception ex) { Console.Error.WriteLine($"Hidden entities save failed: {ex.Message}"); }
    }
}
