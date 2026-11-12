using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace POE2Radar.Overlay.Pricing;

/// <summary>
/// Result of a price lookup. <see cref="Exalted"/> is the item's value in Exalted Orbs (PoE2's base
/// economy unit). <see cref="Quantity"/> is the listing count / trade volume — a confidence signal:
/// low-volume rows are often mislisted (a 2-listing leveling unique priced at 5000 div).
/// </summary>
public readonly record struct PriceResult(string Name, double Exalted, int Quantity, string Category)
{
    public bool LowConfidence(int minQty) => Quantity < minQty;
}

/// <summary>
/// Centralized price source, ported in spirit from the user's PoE1 NinjaPriceService and built around
/// the live <b>poe.ninja</b> PoE2 economy API (the gold-standard source, estimated off the official trade
/// API). Fetches the current league's currency-like + unique prices, converts everything to Exalted, and
/// indexes them by normalized name AND by 2D-art basename (the bridge to in-game item reads: a dropped
/// item's RenderItem .dds basename equals poe.ninja's icon basename), caches to disk, and refreshes on a
/// TTL. Reads are lock-free off volatile snapshots; the whole index is swapped atomically.
///
/// <para>Two poe.ninja endpoints, both returning all rows in one (un-paginated) response:</para>
/// <list type="bullet">
///   <item><b>exchange</b> (<c>.../exchange/current/overview?type=…</c>) — fungible/currency-like rows.
///   <c>items[]</c> maps id→name/image, <c>lines[]</c> carries the price; joined by id.</item>
///   <item><b>stash item</b> (<c>.../stash/current/item/overview?type=Unique…</c>) — uniques, with
///   name/icon/price/listingCount all inline on each <c>lines[]</c> row.</item>
/// </list>
///
/// <para>Price unit: every line's <c>primaryValue</c> is in <b>Divine Orbs</b> (validated against known
/// anchors — Divine=1, Exalted≈0.0066, and the dirt-cheap unique floor ≈0.0017 div), and
/// <c>core.rates.exalted</c> is Exalted-per-Divine, so <c>ex = primaryValue × rates.exalted</c> uniformly.</para>
///
/// <para>poe.ninja has no clean league-list endpoint, so the current league NAME is still discovered via
/// poe2scout's lightweight <c>/Leagues</c> (its <c>Value</c> strings match what poe.ninja expects, e.g.
/// "Runes of Aldur"); the divine/chaos RATES come from poe.ninja itself so prices stay self-consistent.</para>
///
/// <para>This is the recyclable data source: ground-loot valuation, runeforge rewards, expedition choices,
/// ritual/vendor overlays all consume <see cref="TryByArt"/> / <see cref="TryByName"/>.</para>
/// </summary>
public sealed class PriceBook
{
    // poe.ninja "stash item" overview types (icon → art basename + price inline per line). Mostly
    // uniques, plus PrecursorTablets (the atlas/tower tablets — Overseer/Breach/Ritual/… Tablet), which
    // share this endpoint's shape rather than the exchange one.
    private static readonly string[] UniqueTypes =
        { "UniqueWeapons", "UniqueArmours", "UniqueAccessories", "UniqueFlasks", "UniqueJewels",
          "UniqueTablets", "PrecursorTablets" };

    // poe.ninja "exchange" currency-like types. Superset of the old poe2scout set — includes
    // SoulCores (poe2scout's "ultimatum"), UncutGems (Uncut Skill/Spirit/Support Gem by level) and
    // LineageSupportGems (named, tradeable). NOTE: individual CUT active skill gems (e.g. "Rain of
    // Blades") are not a traded market on poe.ninja or anywhere — only UncutGems carry a value.
    // "Verisium" is the Expedition-2 / rune-crafting currency family (Runes of Aldur): the Alloys
    // (Mystic/Prismatic/Swift/Runic/… Alloy), Verisium, Starlit Ores, and Crests — the runeforge reward
    // pool. Without it those rewards have no price and draw no overlay label.
    private static readonly string[] ExchangeTypes =
        { "Currency", "Runes", "Fragments", "Essences", "Expedition", "Verisium", "Breach", "Ritual",
          "Delirium", "UncutGems", "Abyss", "SoulCores", "LineageSupportGems", "Idols" };

    private const string NinjaExchange = "https://poe.ninja/poe2/api/economy/exchange/current/overview";
    private const string NinjaStashItem = "https://poe.ninja/poe2/api/economy/stash/current/item/overview";

    private static readonly HttpClient Http = CreateHttp();
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _cachePath;
    private readonly object _gate = new();

    // Atomically-swapped snapshots (volatile; lock-free reads on the tick/render thread).
    private volatile Dictionary<string, PricedItem> _byArt = new(StringComparer.OrdinalIgnoreCase);
    private volatile Dictionary<string, PricedItem> _byName = new(StringComparer.OrdinalIgnoreCase);

    private volatile bool _fetching;
    private DateTime _lastFetchUtc = DateTime.MinValue;
    private string _league = "";
    private string? _leagueOverride;
    private volatile string? _detectedLeague; // league name read from game memory (Poe2Live.LeagueName)

    // Retry scheduling. A failed / empty / partial fetch does NOT wait the full refresh interval, but it
    // must not be retried every tick either (RefreshIfDue runs ~30×/s): _retryPending marks "try again
    // without waiting for the TTL", _retryNotBeforeTicks is the earliest UTC time that retry may start, and
    // _failStreak drives the exponential backoff between attempts (1, 2, 4… min, capped at the interval).
    private volatile bool _retryPending;
    private long _retryNotBeforeTicks;
    private int _failStreak;
    // Bumped whenever the REQUESTED league changes (override or detected). A fetch snapshots it up front;
    // if it moved by the time the fetch finishes, the result may be for the wrong league (see Superseded).
    private int _leagueGen;
    // True once ExPerDivine holds a REAL rate (from a response or the disk cache) rather than its
    // placeholder default of 1 — only then may a rate-less fetch fall back to it.
    private bool _rateKnown;

    public double ExPerDivine { get; private set; } = 1;
    public double ExPerChaos { get; private set; } = 1;
    public bool IsLoaded => _byName.Count > 0 || _byArt.Count > 0;
    public int ItemCount => _byArt.Count + _byName.Count;
    public string League => _league;
    public string Status { get; private set; } = "not started";
    public DateTime LastFetchUtc => _lastFetchUtc;
    public int RefreshIntervalMinutes { get; set; } = 30;

    private sealed record PricedItem(string Name, double Exalted, int Quantity, string Category);

    public PriceBook(string cachePath, string? leagueOverride = null)
    {
        _cachePath = cachePath;
        _leagueOverride = string.IsNullOrWhiteSpace(leagueOverride) ? null : leagueOverride.Trim();
        TryLoadCache();
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("POE2Radar-PriceBook");
        return c;
    }

    /// <summary>Set/clear a manual league (e.g. from settings); triggers a refresh if it changed.</summary>
    public void SetLeagueOverride(string? league)
    {
        var v = string.IsNullOrWhiteSpace(league) ? null : league.Trim();
        if (v == _leagueOverride) return;
        _leagueOverride = v;
        LeagueRequestChanged();
    }

    /// <summary>Report the league read from game memory (Poe2Live.LeagueName, e.g. "HC Runes of Aldur").
    /// When no manual override is set, this is what we price against — it disambiguates the softcore vs
    /// hardcore league that poe2scout reports both as current. Triggers a re-fetch when it changes. A
    /// blank/null read means "couldn't read it this time" (e.g. ServerData not resolved yet), NOT "no
    /// league" — the last known league is kept, so a transient blank can't cause a refetch storm.</summary>
    public void SetDetectedLeague(string? league)
    {
        var v = string.IsNullOrWhiteSpace(league) ? null : league.Trim();
        if (v == null || v == _detectedLeague) return;
        _detectedLeague = v;
        if (_leagueOverride == null) LeagueRequestChanged(); // only matters when auto-detecting
    }

    /// <summary>The league we price against just changed: make the next <see cref="RefreshIfDue"/> fetch
    /// straight away (no TTL, no failure backoff — a corrected league must not wait out the old one's
    /// retry delay), and bump <see cref="_leagueGen"/> so a fetch already in flight for the OLD league
    /// re-fetches when it completes instead of stamping itself fresh (see <see cref="Superseded"/>).</summary>
    private void LeagueRequestChanged()
    {
        Interlocked.Increment(ref _leagueGen);
        _lastFetchUtc = DateTime.MinValue; // force the next RefreshIfDue to re-fetch
        _failStreak = 0;
        Volatile.Write(ref _retryNotBeforeTicks, 0);
    }

    /// <summary>Call periodically (cheap when not due). Kicks a background fetch when stale (or a retry is
    /// pending) and not already running; after a failed/partial fetch it waits out the retry backoff.</summary>
    public void RefreshIfDue()
    {
        if (_fetching) return;
        var now = DateTime.UtcNow;
        if (!_retryPending && now - _lastFetchUtc < TimeSpan.FromMinutes(RefreshIntervalMinutes)) return;
        if (now.Ticks < Volatile.Read(ref _retryNotBeforeTicks)) return; // backing off after a failure
        _fetching = true;
        _ = Task.Run(FetchAsync);
    }

    /// <summary>Fetch now, ignoring both the TTL and any failure backoff (an explicit user action).</summary>
    public void ForceRefresh()
    {
        if (_fetching) return;
        _fetching = true;
        _ = Task.Run(FetchAsync);
    }

    /// <summary>Look up a unique by its 2D-art basename (e.g. "Earthbound") — the in-game item read key.</summary>
    public PriceResult? TryByArt(string? artBasename)
    {
        if (string.IsNullOrWhiteSpace(artBasename)) return null;
        return _byArt.TryGetValue(artBasename.Trim(), out var p)
            ? new PriceResult(p.Name, p.Exalted, p.Quantity, p.Category) : null;
    }

    /// <summary>Look up any priced item (unique or currency) by display name.</summary>
    public PriceResult? TryByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _byName.TryGetValue(Normalize(name), out var p)
            ? new PriceResult(p.Name, p.Exalted, p.Quantity, p.Category) : null;
    }

    /// <summary>Convert an Exalted value to a short display string in the largest sensible unit.</summary>
    public string Format(double ex)
    {
        if (ExPerDivine > 1 && ex >= ExPerDivine) return $"{ex / ExPerDivine:0.##} div";
        return $"{ex:0.##} ex";
    }

    // ── fetch ────────────────────────────────────────────────────────────────

    private enum FetchOutcome
    {
        Failed,      // nothing usable (no league / network down / no rows / no rate) → back off, then retry
        Partial,     // published, but at least one category request failed → retry sooner than the TTL
        Ok,          // every category answered → next fetch after the normal refresh interval
        Superseded,  // the requested league changed mid-fetch and this result isn't for it → refetch now
    }

    /// <summary>One downloaded overview page, held until the whole fetch is in (see <see cref="FetchAsync"/>).</summary>
    private readonly record struct Page(string Type, bool Exchange, NinjaOverview Data);

    private async Task FetchAsync()
    {
        var outcome = FetchOutcome.Failed;
        var gen = Volatile.Read(ref _leagueGen);   // the league request this fetch is answering
        var league = "";
        try
        {
            Status = "fetching…";
            league = await ResolveLeagueAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(league)) { Status = "no league"; return; }
            var lg = Uri.EscapeDataString(league);

            // Phase 1 — download every overview BEFORE pricing anything. A category whose request FAILED
            // (timeout / 429 / 5xx / unparseable body) is remembered so its previous prices can be kept;
            // a category the league simply doesn't have (404 / no lines) is just empty.
            var pages = new List<Page>(ExchangeTypes.Length + UniqueTypes.Length);
            var failed = new List<string>();
            foreach (var type in ExchangeTypes)
            {
                var (data, fail) = await GetOverviewAsync($"{NinjaExchange}?league={lg}&type={type}").ConfigureAwait(false);
                if (fail) failed.Add(type);
                else if (data?.Lines != null) pages.Add(new Page(type, true, data));
            }
            foreach (var type in UniqueTypes)
            {
                var (data, fail) = await GetOverviewAsync($"{NinjaStashItem}?league={lg}&type={type}").ConfigureAwait(false);
                if (fail) failed.Add(type);
                else if (data?.Lines != null) pages.Add(new Page(type, false, data));
            }

            // The league we were asked for changed while we were downloading and this isn't it: drop the
            // result and let the finally block schedule an immediate refetch for the right league.
            if (Superseded(gen, league)) { outcome = FetchOutcome.Superseded; Status = "league changed — refetching"; return; }

            // Nothing to price: every request failed, or the league has no economy data at all (poe.ninja
            // answers an unknown league with empty overviews — typically a mistyped league override).
            if (!pages.Any(pg => pg.Data.Lines is { Count: > 0 }))
            {
                Status = pages.Count == 0 && failed.Count > 0
                    ? $"fetch failed: all {failed.Count} price requests failed for '{league}'"
                    : $"fetch returned no rows for '{league}'";
                return;
            }

            // Phase 2 — the Exalted-per-Divine rate. Take it from the first response that actually CARRIES
            // core.rates (a response without them must not pin the rate — keep looking). Only if no response
            // in the whole fetch had one do we fall back to the last REAL rate we knew (disk cache / an
            // earlier fetch) — never to the placeholder default: pricing with a made-up rate would publish
            // Divine values labelled as Exalted.
            double exPerDivine = 0, exPerChaos = 0;
            foreach (var p in pages)
                if (TryReadRates(p.Data.Core, out exPerDivine, out exPerChaos)) break;
            var rateFromCache = false;
            if (exPerDivine <= 0)
            {
                if (!_rateKnown) { Status = $"fetch failed: no exchange rate in any response for '{league}'"; return; }
                exPerDivine = ExPerDivine; exPerChaos = ExPerChaos; rateFromCache = true;
            }

            // Phase 3 — price every line with that one rate.
            var byArt = new Dictionary<string, PricedItem>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, PricedItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pages)
            {
                try
                {
                    if (p.Exchange) AddExchange(p.Type, p.Data, exPerDivine, byArt, byName);
                    else AddUniques(p.Type, p.Data, exPerDivine, byArt, byName);
                }
                catch { failed.Add(p.Type); } // malformed page — treat like a failed request (keep previous prices)
            }

            if (byArt.Count == 0 && byName.Count == 0) { Status = $"fetch returned no rows for '{league}'"; return; }

            // A failed category keeps its entries from the PREVIOUS index (same league only — another
            // league's prices are not a substitute) instead of vanishing until the next refresh.
            var sameLeague = string.Equals(_league, league, StringComparison.OrdinalIgnoreCase);
            if (failed.Count > 0 && sameLeague)
            {
                var lost = new HashSet<string>(failed, StringComparer.Ordinal);
                foreach (var kv in _byName) if (lost.Contains(kv.Value.Category)) byName.TryAdd(kv.Key, kv.Value);
                foreach (var kv in _byArt) if (lost.Contains(kv.Value.Category)) byArt.TryAdd(kv.Key, kv.Value);
            }

            _byArt = byArt;
            _byName = byName;
            _league = league;
            ExPerDivine = exPerDivine;
            if (exPerChaos > 0) ExPerChaos = exPerChaos;
            _rateKnown = true;
            _lastFetchUtc = DateTime.UtcNow;
            var status = $"loaded {byName.Count} by name + {byArt.Count} by art for '{league}'";
            if (rateFromCache) status += " (no exchange rate in the response — using the last known rate)";
            if (failed.Count > 0)
                status += $"; {failed.Count} of {ExchangeTypes.Length + UniqueTypes.Length} categories failed ({string.Join(", ", failed)}) — "
                          + (sameLeague ? "kept their previous prices" : "not priced") + ", retrying soon";
            Status = status;
            // Don't replace a complete on-disk cache for this league with a result that lost categories;
            // a partial result for a DIFFERENT league than the cached one is still the better thing to keep.
            if (failed.Count == 0 || !sameLeague) SaveCache();
            outcome = failed.Count == 0 ? FetchOutcome.Ok : FetchOutcome.Partial;
        }
        catch (Exception ex) { Status = $"fetch failed: {ex.Message}"; }
        finally
        {
            // A league change that landed after the check above (or while a doomed fetch was failing) must
            // still win: never let this fetch's completion stand in for the league now being asked for.
            if (outcome != FetchOutcome.Superseded && Superseded(gen, league)) outcome = FetchOutcome.Superseded;

            switch (outcome)
            {
                case FetchOutcome.Ok:
                    _failStreak = 0; _retryPending = false; Volatile.Write(ref _retryNotBeforeTicks, 0);
                    break;
                case FetchOutcome.Superseded:
                    _failStreak = 0; _retryPending = true; Volatile.Write(ref _retryNotBeforeTicks, 0);
                    break;
                default: // Failed / Partial — retry, but with exponential backoff rather than every tick
                    _retryPending = true;
                    Volatile.Write(ref _retryNotBeforeTicks, (DateTime.UtcNow + BackoffDelay(++_failStreak)).Ticks);
                    break;
            }
            _fetching = false;
        }
    }

    /// <summary>Delay before retrying after the <paramref name="streak"/>-th consecutive failed/partial
    /// fetch: 1, 2, 4, 8… minutes, capped at the normal refresh interval.</summary>
    private TimeSpan BackoffDelay(int streak)
    {
        var cap = Math.Max(1, RefreshIntervalMinutes);
        var minutes = Math.Min(cap, Math.Pow(2, Math.Clamp(streak - 1, 0, 16)));
        return TimeSpan.FromMinutes(minutes);
    }

    /// <summary>True when the league request changed since a fetch that began at generation
    /// <paramref name="gen"/> and that fetch's resolved <paramref name="league"/> is not the one now
    /// wanted. (A change that still resolves to the same league — e.g. the startup fetch picked the current
    /// softcore league and the game then reported exactly that — is NOT superseded: no pointless refetch.)</summary>
    private bool Superseded(int gen, string league)
    {
        if (Volatile.Read(ref _leagueGen) == gen) return false;
        var want = _leagueOverride ?? _detectedLeague;
        return want == null || !string.Equals(want, league, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>GET + parse one poe.ninja overview. <c>Failed</c> = a transient/unknown error (timeout, 429,
    /// 5xx, network, unparseable body) — the caller keeps that category's previous prices. A 404/400 means
    /// the league has no such category: not a failure, just no data.</summary>
    private static async Task<(NinjaOverview? Data, bool Failed)> GetOverviewAsync(string url)
    {
        try
        {
            var json = await Http.GetStringAsync(url).ConfigureAwait(false);
            return (JsonSerializer.Deserialize<NinjaOverview>(json, Json), false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest)
        {
            return (null, false);
        }
        catch
        {
            return (null, true);
        }
    }

    /// <summary>Resolve the league to price against. Precedence: (1) a manual override from settings;
    /// (2) the league read from game memory (<see cref="SetDetectedLeague"/>) — validated against
    /// poe2scout's /Leagues so we use its canonical Value casing, but used as-is if the list is
    /// unreachable, since the in-game string already matches poe.ninja verbatim; (3) fallback to the
    /// current softcore league from /Leagues. poe2scout's Value strings are exactly what poe.ninja expects.</summary>
    private async Task<string> ResolveLeagueAsync()
    {
        if (_leagueOverride != null) return _leagueOverride;
        var detected = _detectedLeague;
        List<ScoutLeague> leagues;
        try
        {
            var json = await Http.GetStringAsync("https://poe2scout.com/api/poe2/Leagues").ConfigureAwait(false);
            leagues = JsonSerializer.Deserialize<List<ScoutLeague>>(json, Json) ?? new();
        }
        catch
        {
            return detected ?? ""; // list unreachable — trust the in-game string (already poe.ninja-shaped)
        }

        if (detected != null)
        {
            var match = leagues.FirstOrDefault(l => string.Equals(l.Value, detected, StringComparison.OrdinalIgnoreCase));
            return match?.Value ?? detected; // canonical casing if known, else the raw in-game name
        }

        var pick = leagues.FirstOrDefault(l => l.IsCurrent && !l.Value.StartsWith("HC", StringComparison.OrdinalIgnoreCase))
                   ?? leagues.FirstOrDefault(l => l.IsCurrent)
                   ?? leagues.FirstOrDefault();
        return pick?.Value ?? "";
    }

    // Convert poe.ninja core.rates → our Exalted-per-Divine / Exalted-per-Chaos. rates are "units per
    // Divine": exalted = ex/div directly; chaos = chaos/div, so ex/chaos = (ex/div)/(chaos/div).
    // Returns false (and zeroes) when the response carries no usable exalted rate, so the caller keeps
    // looking at later responses rather than pricing with a default. Nothing is published from here —
    // FetchAsync commits ExPerDivine/ExPerChaos together with the index.
    private static bool TryReadRates(NinjaCore? core, out double exPerDivine, out double exPerChaos)
    {
        exPerDivine = 0; exPerChaos = 0;
        if (core?.Rates == null) return false;
        if (!core.Rates.TryGetValue("exalted", out var exPerDiv) || !(exPerDiv > 0) || !double.IsFinite(exPerDiv)) return false;
        exPerDivine = exPerDiv;
        if (core.Rates.TryGetValue("chaos", out var chaosPerDiv) && chaosPerDiv > 0)
            exPerChaos = exPerDiv / chaosPerDiv;
        return true;
    }

    /// <summary>Price one downloaded EXCHANGE overview (currency-like rows) into the new index at
    /// <paramref name="rate"/> Exalted per Divine.</summary>
    private static void AddExchange(string type, NinjaOverview data, double rate,
        Dictionary<string, PricedItem> byArt, Dictionary<string, PricedItem> byName)
    {
        if (data.Lines == null) return;

        // items[] maps id → name/image; join the priced lines[] to it by id.
        var meta = new Dictionary<string, NinjaItem>(StringComparer.Ordinal);
        if (data.Items != null)
            foreach (var it in data.Items)
                if (!string.IsNullOrEmpty(it.Id)) meta[it.Id] = it;

        foreach (var ln in data.Lines)
        {
            if (ln.Id.ValueKind != JsonValueKind.String) continue; // exchange ids are strings
            var id = ln.Id.GetString();
            if (string.IsNullOrEmpty(id) || !meta.TryGetValue(id, out var m)) continue;
            if (string.IsNullOrWhiteSpace(m.Name) || ln.PrimaryValue <= 0) continue;
            var ex = ln.PrimaryValue * rate;
            var qty = (int)Math.Clamp(ln.VolumePrimaryValue ?? 0, 0, int.MaxValue);
            var item = new PricedItem(m.Name.Trim(), ex, qty, type);
            Upsert(byName, Normalize(m.Name), item);
            var art = ArtBasenameFromIcon(m.Image);
            if (art != null) Upsert(byArt, art, item, preferVolume: true);
        }
    }

    /// <summary>Price one downloaded STASH-ITEM overview (uniques / tablets — name, icon and price inline
    /// per line) into the new index at <paramref name="rate"/> Exalted per Divine.</summary>
    private static void AddUniques(string type, NinjaOverview data, double rate,
        Dictionary<string, PricedItem> byArt, Dictionary<string, PricedItem> byName)
    {
        if (data.Lines == null) return;

        foreach (var ln in data.Lines)
        {
            if (string.IsNullOrWhiteSpace(ln.Name) || ln.PrimaryValue <= 0) continue;
            var ex = ln.PrimaryValue * rate;
            var item = new PricedItem(ln.Name.Trim(), ex, ln.ListingCount ?? 0, type);
            Upsert(byName, Normalize(ln.Name), item);
            var art = ArtBasenameFromIcon(ln.Icon);
            if (art != null) Upsert(byArt, art, item, preferVolume: true);
        }
    }

    /// <summary>Insert/replace on a key collision. By NAME (unique keys) keep the higher value. By ART,
    /// currency TIERS share one icon (Exalted/Greater/Perfect Exalted → same "CurrencyAddModToRare" art),
    /// so keeping the most VALUABLE made every plain Exalted inherit Perfect Exalted's price — instead keep
    /// the MOST-TRADED listing (<paramref name="preferVolume"/>), which is the common drop.</summary>
    private static void Upsert(Dictionary<string, PricedItem> map, string key, PricedItem item, bool preferVolume = false)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!map.TryGetValue(key, out var cur)) { map[key] = item; return; }
        var replace = preferVolume
            ? item.Quantity > cur.Quantity || (item.Quantity == cur.Quantity && item.Exalted > cur.Exalted)
            : item.Exalted > cur.Exalted;
        if (replace) map[key] = item;
    }

    /// <summary>poe.ninja icon → basename. ".../<hash>/Earthbound.png" → "Earthbound" (handles both the
    /// absolute web.poecdn.com unique icons and the relative "/gen/image/.../X.png" currency images).</summary>
    private static string? ArtBasenameFromIcon(string? iconUrl)
    {
        if (string.IsNullOrWhiteSpace(iconUrl)) return null;
        var noQuery = iconUrl.Split('?')[0];
        var seg = noQuery.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(seg)) return null;
        var dot = seg.LastIndexOf('.');
        var name = dot > 0 ? seg[..dot] : seg;
        return name.Length >= 2 ? name : null;
    }

    private static string Normalize(string s) => s.Trim();

    // ── disk cache ─────────────────────────────────────────────────────────────

    private sealed class CacheDto
    {
        public string League { get; set; } = "";
        public DateTime FetchedUtc { get; set; }
        public double ExPerDivine { get; set; }
        public double ExPerChaos { get; set; }
        public Dictionary<string, PricedItem> ByArt { get; set; } = new();
        public Dictionary<string, PricedItem> ByName { get; set; } = new();
    }

    private void TryLoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) { Status = "no cache; will fetch"; return; }
            var dto = JsonSerializer.Deserialize<CacheDto>(File.ReadAllText(_cachePath), Json);
            if (dto == null) return;
            // Honor a configured league: a cache for a different league shouldn't be used.
            if (_leagueOverride != null && !string.Equals(dto.League, _leagueOverride, StringComparison.OrdinalIgnoreCase)) return;
            _byArt = new Dictionary<string, PricedItem>(dto.ByArt, StringComparer.OrdinalIgnoreCase);
            _byName = new Dictionary<string, PricedItem>(dto.ByName, StringComparer.OrdinalIgnoreCase);
            _league = dto.League;
            _lastFetchUtc = dto.FetchedUtc;
            if (dto.ExPerDivine > 0) { ExPerDivine = dto.ExPerDivine; _rateKnown = true; }
            if (dto.ExPerChaos > 0) ExPerChaos = dto.ExPerChaos;
            Status = $"cache: {ItemCount} entries for '{_league}'";
        }
        catch (Exception ex) { Status = $"cache load failed: {ex.Message}"; }
    }

    private void SaveCache()
    {
        try
        {
            var dto = new CacheDto
            {
                League = _league, FetchedUtc = _lastFetchUtc, ExPerDivine = ExPerDivine, ExPerChaos = ExPerChaos,
                ByArt = new(_byArt), ByName = new(_byName),
            };
            Config.AtomicFile.WriteAllText(_cachePath, JsonSerializer.Serialize(dto, Json));
        }
        catch { }
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────────

    // poe2scout /Leagues — used only to discover the current league name.
    private sealed class ScoutLeague
    {
        public string Value { get; set; } = "";
        public bool IsCurrent { get; set; }
    }

    // poe.ninja overview (both exchange + stash item share this shape).
    private sealed class NinjaOverview
    {
        public NinjaCore? Core { get; set; }
        public List<NinjaLine>? Lines { get; set; }
        public List<NinjaItem>? Items { get; set; }   // exchange only (id→name/image); empty for uniques
    }
    private sealed class NinjaCore
    {
        public Dictionary<string, double>? Rates { get; set; }   // "units per Divine": exalted, chaos
        public string? Primary { get; set; }
        public string? Secondary { get; set; }
    }
    private sealed class NinjaItem
    {
        public string Id { get; set; } = "";       // string id ("exalted", "divine", …) on exchange
        public string Name { get; set; } = "";
        public string? Image { get; set; }
    }
    private sealed class NinjaLine
    {
        // id is a string on the exchange endpoint and a number on the uniques endpoint — read raw and
        // branch on ValueKind (uniques carry name/icon inline so their id is unused).
        public JsonElement Id { get; set; }
        public string? Name { get; set; }            // uniques: display name
        public string? Icon { get; set; }            // uniques: absolute art url
        public double PrimaryValue { get; set; }     // value in Divine Orbs (× rates.exalted → Exalted)
        public double? VolumePrimaryValue { get; set; } // exchange: trade volume (confidence)
        public int? ListingCount { get; set; }       // uniques: listing count (confidence)
    }
}
