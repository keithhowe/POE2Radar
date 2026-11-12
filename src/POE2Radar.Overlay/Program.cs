using POE2Radar.Core;
using POE2Radar.Core.Game;
using POE2Radar.Overlay;

// Must run before ANY window is created: without it the process is DPI-unaware, so on a monitor scaled
// above 100% Windows hands us virtualized (scaled-down) window rects and bitmap-stretches the overlay
// (blurry, and "1 overlay pixel == 1 game pixel" stops being true).
DpiAwareness.EnablePerMonitorV2();

Console.WriteLine("POE2Radar — map/radar overlay");
Console.WriteLine("=============================");

// Say where the offsets came from BEFORE attaching: if a patch broke reads, the first question is
// always "which offset table is this build actually using?".
Console.WriteLine(OffsetOverrides.Describe());

using var process = ProcessHandle.AttachToPoE();
if (process is null)
{
    Console.Error.WriteLine("PoE2 not running (no matching process found).");
    return 1;
}
Console.WriteLine($"Attached to {process.ProcessName} (PID {process.ProcessId})");

var reader = new MemoryReader(process);

var slot = Bootstrap.ResolveGameStateSlot(process, reader);
if (slot == 0)
    return 2;

Console.WriteLine();
Console.WriteLine("Radar running. Open the in-game map to see terrain + entities.");
Console.WriteLine("Atlas: open it in-game; rings are auto-positioned. F10 over a tile = inspect its map/content/biome.");
Console.WriteLine("Ctrl+C to exit.");

using var app = new RadarApp(process, reader, slot);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; app.RequestShutdown(); };
app.Run();

Console.WriteLine("Done.");
return 0;

/// <summary>Opts the process into per-monitor-v2 DPI awareness so every window/cursor rect we read is in
/// physical pixels — the same space the (DPI-aware) game renders in.</summary>
internal static class DpiAwareness
{
    private static readonly nint PerMonitorAwareV2 = -4;   // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint value);

    public static void EnablePerMonitorV2()
    {
        // Best-effort: returns false if awareness was already set; the API is missing before Windows 10 1703.
        try { SetProcessDpiAwarenessContext(PerMonitorAwareV2); }
        catch (EntryPointNotFoundException) { }
    }
}
