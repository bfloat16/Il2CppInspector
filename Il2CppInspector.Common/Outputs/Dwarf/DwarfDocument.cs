using System.Text;
using Il2CppInspector.Cpp;
using Il2CppInspector.Model;

namespace Il2CppInspector.Outputs.Dwarf;

internal sealed class DwarfDocument : IDisposable
{
    // LLVM emits one compilation unit per source file; IL2CPP units average a few
    // hundred kilobytes. Batching keeps each unit small enough for consumers that
    // build per-unit state without changing the emitted information.
    private const int DefaultMaxUnitBytes = 256 * 1024;

    private readonly DwarfData info = new();
    private readonly DwarfData strings = new();
    private readonly DwarfData fixups = new();
    private readonly DwarfData line = new();
    private readonly DwarfData aranges = new();
    private readonly DwarfData ranges = new();
    private readonly List<(ulong Low, ulong High)> unitRanges = [];
    private readonly List<(ulong Low, ulong High)> unitDataRanges = [];
    private readonly HashSet<ulong> globalAddresses = [];
    private readonly HashSet<string> globalNames = [];
    private readonly DwarfData symbols = new();
    private readonly DwarfData symbolNames = new();
    private readonly DwarfTypes types;
    private readonly int bits;
    private readonly string arch;
    private readonly string name;
    private readonly Section[] code;
    private readonly Section[] sections;
    private readonly ulong[] boundaries;
    private readonly int maxUnitBytes;
    private bool finished;
    private ulong unitStart;
    private int unitIndex;
    private long unitStmtListPosition;
    private long unitRangesPosition;
    private ulong unitSymbolBytes;
    public int Functions { get; private set; }
    public int TypedFunctions { get; private set; }
    public int Globals { get; private set; }
    public int CompilationUnits => unitIndex;
    public int TypeRecords => types.Count;

    public DwarfDocument(int bits, string arch, string name, Section[] code, IEnumerable<ulong> functionBoundaries, int maxUnitBytes = DefaultMaxUnitBytes, Section[] dataSections = null)
    {
        if (maxUnitBytes < 64)
            throw new ArgumentOutOfRangeException(nameof(maxUnitBytes));
        ElfDebugWriter.Machine(bits, arch);
        this.bits = bits;
        this.arch = arch;
        this.name = name;
        this.code = code;
        sections = code.Concat(dataSections ?? []).ToArray();
        this.maxUnitBytes = maxUnitBytes;
        boundaries = functionBoundaries.Select(Normalize).Distinct().Order().ToArray();
        types = new(info, strings, fixups) { BeforeTopLevel = BreakUnit };
        symbols.Writer.Write(new byte[bits == 64 ? 24 : 16]);
        symbolNames.String("");
        BeginUnit();
    }

    private ulong Normalize(ulong address) => arch == "ARM" ? address & ~1UL : address;

    // Use range lists as LLVM does for units containing discontiguous code.
    private void BeginUnit()
    {
        unitIndex++;
        unitStart = info.Position;
        types.BeginUnit(unitStart);
        info.Writer.Write(0u); // Length, patched when the unit closes.
        info.Writer.Write((ushort)4);
        info.Writer.Write(0u); // DW_AT_abbrev offset
        info.Writer.Write((byte)(bits / 8));
        info.Uleb((byte)DwarfAbbrev.Unit);
        types.Name("Il2CppInspector");
        info.Writer.Write((ushort)4); // DW_LANG_C_plus_plus
        types.Name($"{name}_u{unitIndex}.cpp");
        types.Name("");
        unitStmtListPosition = checked((long)info.Position);
        info.Writer.Write(0u);
        info.Writer.Write(new byte[bits / 8]);
        unitRangesPosition = checked((long)info.Position);
        info.Writer.Write(0u);
        unitRanges.Clear();
        unitDataRanges.Clear();
        unitSymbolBytes = 0;
    }

    private void BreakUnit()
    {
        // Split only between symbols or complete type dependency graphs.
        // Large connected type graphs must not force a separate unit per function.
        if (info.Position - unitStart > (ulong)maxUnitBytes && (unitSymbolBytes == 0 || unitSymbolBytes >= (ulong)maxUnitBytes / 2))
        {
            EndUnit();
            BeginUnit();
        }
    }

    private void EndUnit()
    {
        types.CompleteUnit();
        info.Uleb(0); // Terminate the unit DIE's child list.
        var end = info.Position;
        var spans = MergeRanges(unitRanges);
        var addressSpans = MergeRanges(unitRanges.Concat(unitDataRanges));
        var lineOffset = line.Position;
        var rangeOffset = ranges.Position;
        var resume = info.Stream.Position;
        info.Stream.Position = checked((long)unitStart);
        info.Writer.Write(checked((uint)(end - unitStart - 4)));
        info.Stream.Position = unitStmtListPosition;
        info.Writer.Write(checked((uint)lineOffset));
        info.Stream.Position = unitRangesPosition;
        info.Writer.Write(checked((uint)rangeOffset));
        info.Stream.Position = resume;
        foreach (var (low, high) in spans)
        {
            ranges.Address(low, bits);
            ranges.Address(high, bits);
        }
        ranges.Address(0, bits);
        ranges.Address(0, bits);
        WriteLineTable(spans.Count == 0 ? 0 : spans[0].Low);
        if (addressSpans.Count != 0)
            WriteAranges(unitStart, addressSpans);
    }

    private static List<(ulong Low, ulong High)> MergeRanges(IEnumerable<(ulong Low, ulong High)> ranges)
    {
        var spans = new List<(ulong Low, ulong High)>();
        foreach (var span in ranges.OrderBy(r => r.Low))
        {
            if (spans.Count != 0 && span.Low <= spans[^1].High)
                spans[^1] = (spans[^1].Low, Math.Max(spans[^1].High, span.High));
            else
                spans.Add(span);
        }
        return spans;
    }

    private void WriteLineTable(ulong address)
    {
        var start = line.Position;
        line.Writer.Write(0u);
        line.Writer.Write((ushort)4);
        var headerLengthPosition = line.Position;
        line.Writer.Write(0u);
        line.Writer.Write((byte)1); // minimum_instruction_length
        line.Writer.Write((byte)1); // maximum_operations_per_instruction
        line.Writer.Write((byte)1); // default_is_stmt
        line.Writer.Write(unchecked((byte)-5)); // line_base
        line.Writer.Write((byte)14); // line_range
        line.Writer.Write((byte)13); // opcode_base
        foreach (var length in new byte[] { 0, 1, 1, 1, 1, 0, 0, 0, 1, 0, 0, 1 })
            line.Writer.Write(length);
        line.Writer.Write((byte)0); // Empty include directory list.
        line.Writer.Write(Encoding.UTF8.GetBytes("il2cpp.cpp"));
        line.Writer.Write((byte)0);
        line.Writer.Write((byte)0); // directory index
        line.Writer.Write((byte)0); // modification time
        line.Writer.Write((byte)0); // file length
        line.Writer.Write((byte)0); // End of file names.
        var headerLength = line.Position - headerLengthPosition - 4;
        line.Writer.Write((byte)0); // DW_LNE_set_address
        line.Uleb((ulong)(bits / 8 + 1));
        line.Writer.Write((byte)2);
        line.Address(address, bits);
        line.Writer.Write((byte)0); // DW_LNE_end_sequence
        line.Writer.Write((byte)1);
        line.Writer.Write((byte)1);
        var end = line.Position;
        var resume = line.Stream.Position;
        line.Stream.Position = checked((long)start);
        line.Writer.Write(checked((uint)(end - start - 4)));
        line.Stream.Position = checked((long)headerLengthPosition);
        line.Writer.Write(checked((uint)headerLength));
        line.Stream.Position = resume;
    }

    private void WriteAranges(ulong unitOffset, List<(ulong Low, ulong High)> spans)
    {
        var start = aranges.Position;
        aranges.Writer.Write(0u);
        aranges.Writer.Write((ushort)2);
        aranges.Writer.Write(checked((uint)unitOffset));
        aranges.Writer.Write((byte)(bits / 8));
        aranges.Writer.Write((byte)0);
        var entrySize = bits / 8 * 2;
        while ((aranges.Position - start) % (ulong)entrySize != 0)
            aranges.Writer.Write((byte)0);
        foreach (var (low, high) in spans)
        {
            aranges.Address(low, bits);
            aranges.Address(high - low, bits);
        }
        aranges.Address(0, bits);
        aranges.Address(0, bits);
        var end = aranges.Position;
        var resume = aranges.Stream.Position;
        aranges.Stream.Position = checked((long)start);
        aranges.Writer.Write(checked((uint)(end - start - 4)));
        aranges.Stream.Position = resume;
    }

    public void Method(NativeMethod method)
    {
        if (finished)
            throw new InvalidOperationException("The DWARF document has already been written.");
        var address = Normalize(method.Address);
        if (address == 0)
            return;
        var sectionIndex = Array.FindIndex(code, s => address >= s.VirtualStart && address <= s.VirtualEnd);
        if (sectionIndex < 0)
            return;
        var section = code[sectionIndex];
        var next = Array.BinarySearch(boundaries, address);
        next = next >= 0 ? next + 1 : ~next;
        var extent = section.VirtualEnd - address + 1;
        if (next < boundaries.Length)
            extent = Math.Min(extent, boundaries[next] - address);
        extent = Math.Min(extent, 0x200000UL);
        var typed = method.SignatureComplete && method.Signature != null;
        var name = method.LinkageName ?? method.Name;
        BreakUnit();
        uint returnType = 0;
        uint[] parameterTypes = [];
        if (typed)
        {
            returnType = types.Resolve(method.Signature.ReturnType);
            parameterTypes = method.Signature.Arguments.Select(a => types.Resolve(a.Type)).ToArray();
            types.Emit(returnType);
            foreach (var parameter in parameterTypes)
                types.Emit(parameter);
            types.Drain();
        }
        var dieStart = info.Position;
        info.Uleb((byte)(typed ? DwarfAbbrev.Function : DwarfAbbrev.UntypedFunction));
        types.Name(name, false);
        types.Name(name, false);
        info.Address(address, bits);
        info.Writer.Write(extent);
        if (typed)
        {
            types.Reference(returnType);
            for (var i = 0; i < parameterTypes.Length; i++)
            {
                info.Uleb((byte)DwarfAbbrev.Parameter);
                types.Name(method.Signature.Arguments[i].Name);
                types.Reference(parameterTypes[i]);
            }
            info.Uleb(0);
            TypedFunctions++;
        }
        unitSymbolBytes += info.Position - dieStart;
        unitRanges.Add((address, checked(address + extent)));
        var symbolName = checked((uint)symbolNames.String(name));
        symbols.Writer.Write(symbolName);
        if (bits == 64)
        {
            symbols.Writer.Write((byte)0x02); // STB_LOCAL | STT_FUNC: debug names are not exports.
            symbols.Writer.Write((byte)0);
            symbols.Writer.Write(checked((ushort)(sectionIndex + 1)));
            symbols.Writer.Write(address);
            symbols.Writer.Write(extent);
        }
        else
        {
            symbols.Writer.Write(checked((uint)address));
            symbols.Writer.Write(checked((uint)extent));
            symbols.Writer.Write((byte)0x02);
            symbols.Writer.Write((byte)0);
            symbols.Writer.Write(checked((ushort)(sectionIndex + 1)));
        }
        Functions++;
    }

    public void Variable(NativeDebugVariable variable)
    {
        if (finished)
            throw new InvalidOperationException("The DWARF document has already been written.");
        // Data addresses must not receive ARM's instruction-only Thumb normalization.
        var address = variable.Address;
        if (address == 0 || address == ulong.MaxValue || (bits == 32 && address > uint.MaxValue) || variable.Type == null || string.IsNullOrEmpty(variable.Name))
            return;
        var size = variable.Type.SizeBytes;
        if (size <= 0)
            return;
        var sectionIndex = Array.FindIndex(sections, s => !s.IsExec && (s.IsData || s.IsBSS) && address >= s.VirtualStart && address <= s.VirtualEnd && (ulong)size <= s.VirtualEnd - address + 1);
        if (sectionIndex < 0 || !globalAddresses.Add(address))
            return;
        var name = variable.Name;
        while (!globalNames.Add(name))
            name += $"_{address:X}";
        BreakUnit();
        var type = types.Resolve(variable.Type);
        types.Emit(type);
        types.Drain();
        var dieStart = info.Position;
        info.Uleb((byte)DwarfAbbrev.Variable);
        types.Name(name, false);
        types.Name(name, false);
        types.Reference(type);
        info.Uleb((ulong)(bits / 8 + 1)); // DW_FORM_exprloc byte count
        info.Writer.Write((byte)0x03); // DW_OP_addr: a memory location, not a value.
        info.Address(address, bits);
        unitSymbolBytes += info.Position - dieStart;
        unitDataRanges.Add((address, checked(address + (ulong)size)));
        var symbolName = checked((uint)symbolNames.String(name));
        symbols.Writer.Write(symbolName);
        if (bits == 64)
        {
            symbols.Writer.Write((byte)0x11); // STB_GLOBAL | STT_OBJECT
            symbols.Writer.Write((byte)0);
            symbols.Writer.Write(checked((ushort)(sectionIndex + 1)));
            symbols.Writer.Write(address);
            symbols.Writer.Write((ulong)size);
        }
        else
        {
            symbols.Writer.Write(checked((uint)address));
            symbols.Writer.Write(checked((uint)size));
            symbols.Writer.Write((byte)0x11);
            symbols.Writer.Write((byte)0);
            symbols.Writer.Write(checked((ushort)(sectionIndex + 1)));
        }
        Globals++;
    }

    public void Write(string path, IEnumerable<CppType> nativeTypes, IEnumerable<NativeDebugVariable> globals = null, DwarfImageSource source = null)
    {
        if (finished)
            throw new InvalidOperationException("The DWARF document has already been written.");
        types.Include(nativeTypes);
        foreach (var variable in globals ?? [])
            Variable(variable);
        types.Drain();
        EndUnit();
        finished = true;
        ElfDebugWriter.Write(path, bits, arch, sections, info, types, strings, line, aranges, ranges, symbols, symbolNames, source);
    }

    public void Dispose()
    {
        info.Dispose();
        strings.Dispose();
        fixups.Dispose();
        line.Dispose();
        aranges.Dispose();
        ranges.Dispose();
        symbols.Dispose();
        symbolNames.Dispose();
    }
}
