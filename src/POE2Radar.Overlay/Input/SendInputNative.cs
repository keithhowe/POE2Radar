using System.Runtime.InteropServices;

namespace POE2Radar.Overlay.Input;

/// <summary>
/// Minimal keyboard input via Win32 <c>SendInput</c>, scancode-based (KEYEVENTF_SCANCODE) which
/// games read more reliably than virtual-key events. Used by the auto-flask feature; all firing
/// is gated by <see cref="RadarApp"/> (foreground + in-game + cooldown + kill-switch).
/// </summary>
internal static class SendInputNative
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    // VK → scan code, with the extended-key prefix (0xE0 / 0xE1) reported in the high byte.
    private const uint MAPVK_VK_TO_VSC_EX = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    /// <summary>
    /// Press and release a virtual key (e.g. 0x31 = '1') as a scancode keystroke. Returns true only when
    /// Windows reports BOTH events (down + up) injected. Returns false — having sent nothing — when the
    /// key has no single scan code (mouse buttons, Pause, unmapped VKs), and false when the input was
    /// blocked (e.g. UIPI: the game runs elevated and the overlay does not), so the caller can tell a
    /// flask that actually fired from one that did not.
    /// </summary>
    public static bool Tap(ushort vk)
    {
        if (!TryGetScan(vk, out var scan, out var extended)) return false;

        var flags = KEYEVENTF_SCANCODE | (extended ? KEYEVENTF_EXTENDEDKEY : 0u);
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].U.ki = new KEYBDINPUT { wScan = scan, dwFlags = flags };
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].U.ki = new KEYBDINPUT { wScan = scan, dwFlags = flags | KEYEVENTF_KEYUP };

        // Down + up go in ONE call, so they are injected together or not at all — no stuck key.
        return SendInput(2, inputs, Marshal.SizeOf<INPUT>()) == 2;
    }

    /// <summary>
    /// Resolve a virtual key to its scan code and whether it needs KEYEVENTF_EXTENDEDKEY (the 0xE0
    /// prefix). False when the key cannot be sent as one scan code.
    /// </summary>
    private static bool TryGetScan(ushort vk, out ushort scan, out bool extended)
    {
        var mapped = MapVirtualKey(vk, MAPVK_VK_TO_VSC_EX);
        scan = (ushort)(mapped & 0xFF);
        var prefix = (mapped >> 8) & 0xFF;
        extended = prefix == 0xE0;
        if (scan == 0) return false;        // no scan code (mouse buttons, unassigned VKs)
        if (prefix == 0xE1) return false;   // Pause: a multi-byte sequence, not a single scan code

        // The dedicated navigation cluster (PgUp/PgDn/End/Home/arrows/Insert/Delete) and NumLock share
        // their base scan code with a numpad key, and MapVirtualKey reports that shared code WITHOUT the
        // 0xE0 prefix even in the _EX mode — sent as-is, VK_INSERT would arrive as numpad 0, VK_LEFT as
        // numpad 4, and so on. Only the extended flag tells them apart, so set it for these explicitly.
        if (vk is (>= 0x21 and <= 0x28) or 0x2D or 0x2E or 0x90) extended = true;
        return true;
    }
}
