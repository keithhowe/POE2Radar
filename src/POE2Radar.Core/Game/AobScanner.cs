using POE2Radar.Core.Native;

namespace POE2Radar.Core.Game;

/// <summary>
/// Array-of-bytes pattern scanning over PoE.exe's executable image sections.
///
/// Usage flow:
///   Discovery (one-time per build): run POE2Radar.Research --discover-aob with PoE + POEMCP running.
///   That finds stable byte patterns in .text that RIP-reference the IngameState global slot.
///   Paste the output into AobPatterns.cs.
///
///   Boot-time: AobScanner.ScanForResolvedAddresses with a stored pattern finds the global slot
///   address, then ReadPointer(slotAddress) = IngameState. No value-scan bootstrap needed.
/// </summary>
public static class AobScanner
{
    /// <summary>
    /// Find all byte offsets in <paramref name="haystack"/> where the wildcard pattern matches.
    /// A null byte in <paramref name="pattern"/> matches any byte.
    /// </summary>
    public static List<int> FindPattern(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte?> pattern)
    {
        var results = new List<int>();
        if (pattern.Length == 0 || haystack.Length < pattern.Length) return results;

        for (var i = 0; i <= haystack.Length - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (pattern[j] is { } pb && haystack[i + j] != pb)
                {
                    match = false;
                    break;
                }
            }
            if (match) results.Add(i);
        }
        return results;
    }

    /// <summary>
    /// Read all committed executable pages within PoE.exe's main module image.
    /// Returns (absoluteAddress, bytes) for each executable section page group.
    /// </summary>
    public static List<(nint Address, byte[] Bytes)> ReadExecutableSections(ProcessHandle proc, MemoryReader reader)
    {
        var sections = new List<(nint, byte[])>();
        var moduleEnd = proc.MainModuleBase + (nint)proc.MainModuleSize;

        foreach (var mbi in proc.EnumerateRegions(proc.MainModuleBase, moduleEnd))
        {
            if (mbi.State != NativeMethods.MEM_COMMIT) continue;
            if (mbi.Type != NativeMethods.MEM_IMAGE) continue;
            if ((mbi.Protect & NativeMethods.PAGE_GUARD) != 0) continue;

            const uint execMask = NativeMethods.PAGE_EXECUTE
                                | NativeMethods.PAGE_EXECUTE_READ
                                | NativeMethods.PAGE_EXECUTE_READWRITE;
            if ((mbi.Protect & execMask) == 0) continue;

            var size = (int)mbi.RegionSize;
            var buf = new byte[size];
            var read = reader.TryReadBytes(mbi.BaseAddress, buf.AsSpan());
            if (read == 0) continue;

            sections.Add(read == size ? (mbi.BaseAddress, buf) : (mbi.BaseAddress, buf[..read]));
        }
        return sections;
    }

    /// <summary>
    /// Read committed non-executable (data) pages within PoE.exe's main module image.
    /// These hold the global variables that code references via RIP-relative addressing.
    /// </summary>
    public static List<(nint Address, byte[] Bytes)> ReadDataSections(ProcessHandle proc, MemoryReader reader)
    {
        var sections = new List<(nint, byte[])>();
        var moduleEnd = proc.MainModuleBase + (nint)proc.MainModuleSize;

        foreach (var mbi in proc.EnumerateRegions(proc.MainModuleBase, moduleEnd))
        {
            if (mbi.State != NativeMethods.MEM_COMMIT) continue;
            if (mbi.Type != NativeMethods.MEM_IMAGE) continue;
            if ((mbi.Protect & NativeMethods.PAGE_GUARD) != 0) continue;

            const uint execMask = NativeMethods.PAGE_EXECUTE
                                | NativeMethods.PAGE_EXECUTE_READ
                                | NativeMethods.PAGE_EXECUTE_READWRITE;
            if ((mbi.Protect & execMask) != 0) continue;
            if ((mbi.Protect & NativeMethods.READABLE_PROTECT_MASK) == 0) continue;

            var size = (int)mbi.RegionSize;
            var buf = new byte[size];
            var read = reader.TryReadBytes(mbi.BaseAddress, buf.AsSpan());
            if (read < 8) continue;

            sections.Add((mbi.BaseAddress, buf[..read]));
        }
        return sections;
    }

    /// <summary>
    /// Resolve a RIP-relative 4-byte displacement at a known position in a section buffer.
    /// Returns the absolute address in the target process that the instruction references.
    /// </summary>
    /// <param name="sectionBase">Base address of the section in the target process.</param>
    /// <param name="matchOffset">Offset of the instruction start within <paramref name="sectionBytes"/>.</param>
    /// <param name="dispOffset">Offset of the 4-byte signed displacement within the instruction (e.g., 3 for REX MOV reg,[RIP+rel32]).</param>
    /// <param name="instrLen">Total instruction length in bytes (e.g., 7 for 3-byte prefix + 4-byte displacement).</param>
    /// <param name="sectionBytes">Raw bytes of the section.</param>
    public static nint ResolveRipRelative(nint sectionBase, int matchOffset, int dispOffset, int instrLen, ReadOnlySpan<byte> sectionBytes)
    {
        var dispPos = matchOffset + dispOffset;
        if (dispPos + 4 > sectionBytes.Length) return 0;
        var displacement = BitConverter.ToInt32(sectionBytes[dispPos..]);
        var nextInstrAddr = sectionBase + matchOffset + instrLen;
        return nextInstrAddr + displacement;
    }

    /// <summary>
    /// Scan all executable sections of PoE.exe for <paramref name="pattern"/>, returning the absolute
    /// address each match's RIP-relative displacement resolves to. Used at bot startup once patterns
    /// are stored in <see cref="AobPatterns"/>.
    /// </summary>
    public static List<nint> ScanForResolvedAddresses(ProcessHandle proc, MemoryReader reader,
        AobPatterns.Pattern pattern)
    {
        var results = new List<nint>();
        foreach (var (sectionBase, bytes) in ReadExecutableSections(proc, reader))
        {
            var matches = FindPattern(bytes, pattern.Bytes);
            foreach (var matchOffset in matches)
            {
                var addr = ResolveRipRelative(sectionBase, matchOffset, pattern.DispOffset, pattern.InstrLen, bytes);
                if (addr != 0) results.Add(addr);
            }
        }
        return results;
    }

    /// <summary>
    /// Discovery helper: scan PoE.exe's data sections for 8-byte-aligned pointer slots containing
    /// <paramref name="targetAddress"/>. Returns the absolute address of each matching slot.
    /// These slot addresses are the targets of RIP-relative MOV instructions in .text.
    /// </summary>
    public static List<nint> FindGlobalPointerTo(ProcessHandle proc, MemoryReader reader, nint targetAddress)
    {
        var results = new List<nint>();
        var target = (long)targetAddress;

        foreach (var (sectionBase, bytes) in ReadDataSections(proc, reader))
        {
            var span = bytes.AsSpan();
            for (var i = 0; i + 8 <= span.Length; i += 8)
            {
                if (BitConverter.ToInt64(span[i..]) == target)
                    results.Add(sectionBase + i);
            }
        }
        return results;
    }

    /// <summary>
    /// Discovery helper: find every REX.W MOV reg,[RIP+rel32] instruction in .text whose
    /// displacement resolves to <paramref name="globalSlotAddress"/>. Returns rich context
    /// for building the pattern to store in <see cref="AobPatterns"/>.
    ///
    /// Recognized instruction form: [REX.W 0x4x] [0x8B] [ModRM mod=00,rm=101] [rel32]
    /// </summary>
    public static List<AobDiscoveryHit> FindReferencesTo(ProcessHandle proc, MemoryReader reader,
        nint globalSlotAddress, int contextBytes = 24)
    {
        var hits = new List<AobDiscoveryHit>();

        foreach (var (sectionBase, bytes) in ReadExecutableSections(proc, reader))
        {
            var span = bytes.AsSpan();
            for (var i = 0; i + 7 <= bytes.Length; i++)
            {
                var b0 = span[i];
                var b1 = span[i + 1];
                var b2 = span[i + 2];

                // REX.W prefix: 0x48-0x4F (REX with W bit, optional R/X/B bits)
                if (b0 < 0x48 || b0 > 0x4F) continue;
                // MOV r64, r/m64
                if (b1 != 0x8B) continue;
                // ModRM: mod=00 (bits 7-6), rm=101 (bits 2-0) â†’ mask 0xC7 == 0x05
                // bits 5-3 are the destination register â€” any value is valid
                if ((b2 & 0xC7) != 0x05) continue;

                var resolved = ResolveRipRelative(sectionBase, i, 3, 7, span);
                if (resolved != globalSlotAddress) continue;

                var contextStart = Math.Max(0, i - contextBytes);
                var contextEnd = Math.Min(bytes.Length, i + 7 + contextBytes);

                hits.Add(new AobDiscoveryHit(
                    SectionBase: sectionBase,
                    MatchOffset: i,
                    InstructionOffset: i - contextStart,
                    Context: bytes[contextStart..contextEnd],
                    GlobalSlotAddress: globalSlotAddress));
            }
        }

        return hits;
    }

    /// <summary>
    /// Apply a <see cref="AobPatterns.FieldOffsetPattern"/>: scan all executable sections,
    /// extract the displacement bytes from each match, return the offset values found.
    /// Multiple matches → returns all (caller decides which to trust; usually they all give
    /// the same value). Empty list → pattern doesn't match anywhere; signature is stale.
    /// </summary>
    public static List<int> ExtractFieldOffset(ProcessHandle proc, MemoryReader reader,
        AobPatterns.FieldOffsetPattern pattern)
    {
        var results = new List<int>();
        foreach (var (_, bytes) in ReadExecutableSections(proc, reader))
        {
            var matches = FindPattern(bytes, pattern.Bytes);
            foreach (var matchOffset in matches)
            {
                var dispPos = matchOffset + pattern.DispOffsetInMatch;
                int value = pattern.DispWidth switch
                {
                    1 => (sbyte)bytes[dispPos],   // disp8 is sign-extended
                    4 => BitConverter.ToInt32(bytes, dispPos),
                    _ => 0,
                };
                if (value != 0 && !results.Contains(value)) results.Add(value);
            }
        }
        return results;
    }

    /// <summary>
    /// Discovery helper for field offsets: scan PoE.exe's <c>.text</c> for every instance of
    /// <c>mov reg64, [base64+disp32]</c> where <c>base64</c> matches <paramref name="baseRegMask"/>
    /// and the disp32 equals <paramref name="targetOffset"/>. Returns each hit with surrounding
    /// context bytes so a human can pick the unique-enough signature.
    ///
    /// <para>baseRegMask: ModRM byte after MOV opcode. For <c>[RDX+disp32]</c> the ModRM is
    /// <c>10 reg 010</c> = <c>0x82 | (reg<<3)</c>. We mask <c>0xC7</c> to ignore the dest reg.</para>
    /// </summary>
    public static List<FieldAccessHit> FindFieldAccessHits(ProcessHandle proc, MemoryReader reader,
        int targetOffset, byte baseRegRm, int contextBytes = 16)
    {
        var hits = new List<FieldAccessHit>();
        var modRmMask = (byte)0xC7; // mask out dest-reg bits
        var modRmExpect = (byte)(0x80 | baseRegRm); // mod=10 (disp32), rm=baseRegRm

        foreach (var (sectionBase, bytes) in ReadExecutableSections(proc, reader))
        {
            var span = bytes.AsSpan();
            for (var i = 0; i + 7 <= bytes.Length; i++)
            {
                var b0 = span[i];     // REX.W prefix
                var b1 = span[i + 1]; // 8B = MOV r64, r/m64
                var b2 = span[i + 2]; // ModRM
                if (b0 < 0x48 || b0 > 0x4F) continue;
                if (b1 != 0x8B) continue;
                if ((b2 & modRmMask) != modRmExpect) continue;
                var disp = BitConverter.ToInt32(span.Slice(i + 3));
                if (disp != targetOffset) continue;

                var start = Math.Max(0, i - contextBytes);
                var end = Math.Min(bytes.Length, i + 7 + contextBytes);
                hits.Add(new FieldAccessHit(sectionBase, i, i - start, bytes[start..end]));
            }
        }
        return hits;
    }
}

public sealed record FieldAccessHit(nint SectionBase, int MatchOffset, int InstructionOffset, byte[] Context)
{
    /// <summary>Format the hit as a FieldOffsetPattern literal with disp bytes wildcarded.</summary>
    public string FormatPattern(string fieldName, int beforeBytes = 8, int afterBytes = 8)
    {
        var start = Math.Max(0, InstructionOffset - beforeBytes);
        var end   = Math.Min(Context.Length, InstructionOffset + 7 + afterBytes);
        var sb = new System.Text.StringBuilder();
        sb.Append("new AobPatterns.FieldOffsetPattern(\"").Append(fieldName).Append("\", new byte?[] { ");
        var dispOffsetInMatch = -1;
        for (var i = start; i < end; i++)
        {
            var byteOffset = i - InstructionOffset;
            var isDisp = byteOffset is >= 3 and < 7;
            if (isDisp && dispOffsetInMatch < 0) dispOffsetInMatch = i - start;
            sb.Append(isDisp ? "null" : $"0x{Context[i]:X2}");
            if (i < end - 1) sb.Append(", ");
        }
        sb.Append(" }, DispOffsetInMatch: ").Append(dispOffsetInMatch).Append(", DispWidth: 4, Description: \"\")");
        return sb.ToString();
    }
}

public sealed record AobDiscoveryHit(
    nint SectionBase,
    int MatchOffset,
    int InstructionOffset,
    byte[] Context,
    nint GlobalSlotAddress)
{
    /// <summary>
    /// Format this hit as a C# byte?[] pattern literal, with <paramref name="beforeBytes"/> bytes
    /// of context before the instruction and <paramref name="afterBytes"/> after. The 4-byte
    /// RIP displacement (bytes 3-6 of the instruction) is rendered as null wildcards.
    /// </summary>
    public string FormatPattern(int beforeBytes = 8, int afterBytes = 4)
    {
        var start = Math.Max(0, InstructionOffset - beforeBytes);
        var end = Math.Min(Context.Length, InstructionOffset + 7 + afterBytes);

        var sb = new System.Text.StringBuilder();
        sb.Append("new byte?[] { ");

        for (var i = start; i < end; i++)
        {
            var byteOffset = i - InstructionOffset; // offset relative to instruction start
            // Bytes 3-6 of the instruction are the RIP-relative displacement â€” wildcards
            var isDisp = byteOffset is >= 3 and < 7;
            sb.Append(isDisp ? "null" : $"0x{Context[i]:X2}");
            if (i < end - 1) sb.Append(", ");
        }

        sb.Append(" }");
        return sb.ToString();
    }
}
