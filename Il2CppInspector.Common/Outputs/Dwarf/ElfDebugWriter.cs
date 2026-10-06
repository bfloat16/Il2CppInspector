using System.Text;

namespace Il2CppInspector.Outputs.Dwarf;

internal sealed record ElfDebugSection(string Name, uint Type, ulong Flags = 0, ulong Address = 0, ulong Size = 0, DwarfData Data = null, uint Link = 0, uint Info = 0, ulong EntrySize = 0);

internal static class ElfDebugWriter
{
    public static ushort Machine(int bits, string arch) =>
        (bits, arch) switch
        {
            (32, "x86") => 3,
            (64, "x64") => 62,
            (32, "ARM") => 40,
            (64, "ARM64") => 183,
            _ => throw new NotSupportedException("DWARF output requires x86, x64, ARM or ARM64."),
        };

    public static void Write(
        string path,
        int bits,
        string arch,
        Section[] mappedSections,
        DwarfData info,
        DwarfTypes types,
        DwarfData strings,
        DwarfData line,
        DwarfData aranges,
        DwarfData ranges,
        DwarfData symbols,
        DwarfData symbolNames,
        DwarfImageSource source = null
    )
    {
        using var abbreviations = DwarfAbbreviations.Build();
        using var sectionNames = new DwarfData();
        var sections = new List<ElfDebugSection> { new("", 0) };
        sections.AddRange(mappedSections.Select((s, i) => new ElfDebugSection(string.IsNullOrEmpty(s.Name) ? $".mapped.{i}" : s.Name, 8, s.IsExec ? 6UL : 2UL, s.VirtualStart, s.VirtualLength)));
        sections.Add(new(".debug_info", 1, Data: info));
        sections.Add(new(".debug_abbrev", 1, Data: abbreviations));
        sections.Add(new(".debug_line", 1, Data: line));
        sections.Add(new(".debug_aranges", 1, Data: aranges));
        sections.Add(new(".debug_ranges", 1, Data: ranges));
        sections.Add(new(".debug_str", 1, 0x30, Data: strings, EntrySize: 1));
        sections.Add(new(".symtab", 2, Data: symbols, Link: checked((uint)sections.Count + 1), Info: 1, EntrySize: bits == 64 ? 24UL : 16UL));
        sections.Add(new(".strtab", 3, Data: symbolNames));
        if (source != null)
        {
            DwarfImageWriter.Write(path, bits, mappedSections, sections.Where(s => s.Data != null).ToArray(), source);
            return;
        }
        using var orderedSymbols = new ElfDebugSymbols(symbols, bits);
        var symbolIndex = sections.FindIndex(s => s.Data == symbols);
        sections[symbolIndex] = sections[symbolIndex] with { Info = orderedSymbols.LocalCount };
        sections.Add(new(".shstrtab", 3, Data: sectionNames));
        var nameOffsets = sections.Select(s => checked((uint)sectionNames.String(s.Name))).ToArray();
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 65536);
        using var writer = new BinaryWriter(file, Encoding.UTF8, true);
        var headerSize = bits == 64 ? 64 : 52;
        file.SetLength(headerSize);
        file.Position = headerSize;
        var positions = new ulong[sections.Count];
        for (var i = 1; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.Data == null)
                continue;
            Align();
            positions[i] = checked((ulong)file.Position);
            if (section.Data == info)
                types.CopyPatched(file);
            else if (section.Data == symbols)
                orderedSymbols.CopyTo(file);
            else
            {
                section.Data.Writer.Flush();
                section.Data.Stream.Position = 0;
                section.Data.Stream.CopyTo(file);
            }
        }
        Align();
        var sectionHeaderOffset = checked((ulong)file.Position);
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            writer.Write(nameOffsets[i]);
            writer.Write(section.Type);
            Word(section.Flags);
            Word(section.Address);
            Word(positions[i]);
            Word(section.Data == null ? section.Size : checked((ulong)section.Data.Stream.Length));
            writer.Write(section.Link);
            writer.Write(section.Info);
            Word(
                i == 0 ? 0
                : section.Type == 2 ? (ulong)(bits / 8)
                : 1
            );
            Word(section.EntrySize);
        }
        file.Position = 0;
        writer.Write(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', (byte)(bits == 64 ? 2 : 1), 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        writer.Write((ushort)2); // ET_EXEC: symbol addresses are already resolved, not section-relative.
        writer.Write(Machine(bits, arch));
        writer.Write(1u);
        Word(0);
        Word(0);
        Word(sectionHeaderOffset);
        writer.Write(arch == "ARM" ? 0x05000000u : 0u); // ARM EABI version 5.
        writer.Write((ushort)headerSize);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)(bits == 64 ? 64 : 40));
        writer.Write(checked((ushort)sections.Count));
        writer.Write(checked((ushort)(sections.Count - 1)));

        void Word(ulong value)
        {
            if (bits == 64)
                writer.Write(value);
            else
                writer.Write(checked((uint)value));
        }

        void Align()
        {
            while (file.Position % 8 != 0)
                writer.Write((byte)0);
        }
    }
}
