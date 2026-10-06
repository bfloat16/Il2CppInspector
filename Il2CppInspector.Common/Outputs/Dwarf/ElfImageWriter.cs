using System.Buffers.Binary;
using System.Text;

namespace Il2CppInspector.Outputs.Dwarf;

internal static class ElfImageWriter
{
    private sealed class Header
    {
        public string Name = "";
        public uint NameOffset,
            Type,
            Link,
            Info;
        public ulong Flags,
            Address,
            Offset,
            Size,
            Alignment,
            EntrySize;

        public static Header Read(byte[] data, int bits)
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            var header = new Header { NameOffset = reader.ReadUInt32(), Type = reader.ReadUInt32() };
            header.Flags = Word();
            header.Address = Word();
            header.Offset = Word();
            header.Size = Word();
            header.Link = reader.ReadUInt32();
            header.Info = reader.ReadUInt32();
            header.Alignment = Word();
            header.EntrySize = Word();
            return header;

            ulong Word() => bits == 64 ? reader.ReadUInt64() : reader.ReadUInt32();
        }

        public void Write(BinaryWriter writer, int bits)
        {
            writer.Write(NameOffset);
            writer.Write(Type);
            Word(Flags);
            Word(Address);
            Word(Offset);
            Word(Size);
            writer.Write(Link);
            writer.Write(Info);
            Word(Alignment);
            Word(EntrySize);

            void Word(ulong value)
            {
                if (bits == 64)
                    writer.Write(value);
                else
                    writer.Write(checked((uint)value));
            }
        }
    }

    public static void Write(Stream output, int bits, Section[] mapped, IReadOnlyList<ElfDebugSection> debug, DwarfImageSource source)
    {
        var headerSize = bits == 64 ? 64 : 52;
        var entrySize = bits == 64 ? 64 : 40;
        var fileHeader = source.Read(0, headerSize);
        var tableOffset = bits == 64 ? BinaryPrimitives.ReadUInt64LittleEndian(fileHeader.AsSpan(40)) : BinaryPrimitives.ReadUInt32LittleEndian(fileHeader.AsSpan(32));
        var originalEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader.AsSpan(bits == 64 ? 58 : 46));
        var count = (uint)BinaryPrimitives.ReadUInt16LittleEndian(fileHeader.AsSpan(bits == 64 ? 60 : 48));
        var nameIndex = (uint)BinaryPrimitives.ReadUInt16LittleEndian(fileHeader.AsSpan(bits == 64 ? 62 : 50));
        var headers = new List<Header>();
        byte[] oldNames = [0];
        if (tableOffset != 0)
        {
            if (originalEntrySize != entrySize)
                throw new InvalidDataException("Unsupported ELF section header size.");
            var first = Header.Read(source.Read(checked((long)tableOffset), entrySize), bits);
            if (count == 0)
                count = checked((uint)first.Size);
            if (nameIndex == ushort.MaxValue)
                nameIndex = first.Link;
            if (count == 0 || nameIndex >= count || count >= 0xFF00)
                throw new InvalidDataException("Invalid or unsupported ELF section count.");
            for (var i = 0u; i < count; i++)
                headers.Add(Header.Read(source.Read(checked((long)tableOffset + i * entrySize), entrySize), bits));
            if (nameIndex != 0)
            {
                var namesHeader = headers[(int)nameIndex];
                oldNames = source.Read(checked((long)namesHeader.Offset), checked((int)namesHeader.Size));
            }
            foreach (var section in headers)
            {
                if (section.NameOffset >= oldNames.Length)
                    throw new InvalidDataException("Invalid ELF section name offset.");
                var start = checked((int)section.NameOffset);
                var end = Array.IndexOf(oldNames, (byte)0, start);
                if (end < 0)
                    throw new InvalidDataException("Unterminated ELF section name.");
                section.Name = Encoding.UTF8.GetString(oldNames, start, end - start);
            }
        }
        else
            headers.Add(new());

        using var names = new DwarfData();
        names.Writer.Write(oldNames);
        var indices = new ushort[mapped.Length + 1];
        for (var i = 0; i < mapped.Length; i++)
        {
            var section = mapped[i];
            var index = headers.FindIndex(h => (h.Flags & 2) != 0 && h.Address == section.VirtualStart && h.Size >= section.VirtualLength);
            if (index < 0)
            {
                index = Add(string.IsNullOrEmpty(section.Name) ? $".mapped.{i}" : section.Name);
                headers[index].Type = section.IsBSS ? 8u : 1u;
                headers[index].Flags = section.IsExec ? 6u : 3u;
                headers[index].Address = section.VirtualStart;
                headers[index].Offset = checked((ulong)section.ImageStart);
                headers[index].Size = section.VirtualLength;
                headers[index].Alignment = 1;
            }
            indices[i + 1] = checked((ushort)index);
        }

        // Keep original section indices so relocations, groups and dynamic metadata remain valid.
        var generatedNames = debug.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var obsoleteDebug = new HashSet<int>();
        for (var i = 1; i < headers.Count; i++)
        {
            var section = headers[i];
            if (
                (
                    section.Name.StartsWith(".debug_", StringComparison.Ordinal) && section.Name != ".debug_frame"
                    || section.Name.StartsWith(".zdebug_", StringComparison.Ordinal)
                    || section.Name is ".gnu_debuglink" or ".gnu_debugaltlink" or ".gnu_debugdata" or ".gdb_index"
                )
            )
            {
                obsoleteDebug.Add(i);
                if (!generatedNames.Contains(section.Name))
                    headers[i] = new();
            }
        }
        for (var i = 1; i < headers.Count; i++)
            if (headers[i].Type is 4 or 9 && obsoleteDebug.Contains(checked((int)headers[i].Info)))
                headers[i] = new();

        var symbolIndex = headers.FindIndex(h => h.Type == 2 && h.Name == ".symtab");
        Header originalSymbols = symbolIndex < 0 ? null : headers[symbolIndex];
        var stringIndex = originalSymbols == null ? GetOrAdd(".strtab") : checked((int)originalSymbols.Link);
        if (stringIndex <= 0 || stringIndex >= headers.Count || originalSymbols != null && headers[stringIndex].Type != 3)
            throw new InvalidDataException("Invalid ELF symbol string table.");
        var originalStrings = originalSymbols == null ? null : headers[stringIndex];
        if (symbolIndex < 0)
            symbolIndex = Add(".symtab");
        if (nameIndex == 0)
            nameIndex = checked((uint)GetOrAdd(".shstrtab"));
        foreach (var section in debug.Where(s => s.Name.StartsWith(".debug_", StringComparison.Ordinal)))
            GetOrAdd(section.Name);
        if (headers.Count >= 0xFF00)
            throw new InvalidDataException("Too many ELF sections for embedded DWARF.");

        source.CopyTo(output);
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        foreach (var section in debug.Where(s => s.Name.StartsWith(".debug_", StringComparison.Ordinal)))
        {
            DwarfImageWriter.Align(output, bits / 8);
            var index = GetOrAdd(section.Name);
            headers[index] = new Header
            {
                Name = section.Name,
                NameOffset = headers[index].NameOffset,
                Type = section.Type,
                Flags = section.Flags,
                Offset = checked((ulong)output.Position),
                Size = checked((ulong)section.Data.Stream.Length),
                Alignment = 1,
                EntrySize = section.EntrySize,
            };
            DwarfImageWriter.Copy(section.Data, output);
        }

        var stringOffset = checked((ulong)output.Position);
        var originalStringSize = originalStrings?.Size ?? 0;
        if (originalStrings != null)
            source.CopyTo(output, checked((long)originalStrings.Offset), checked((long)originalStrings.Size));
        var generatedStrings = debug.Single(s => s.Name == ".strtab").Data;
        DwarfImageWriter.Copy(generatedStrings, output);
        headers[stringIndex] = new Header
        {
            Name = headers[stringIndex].Name,
            NameOffset = headers[stringIndex].NameOffset,
            Type = 3,
            Offset = stringOffset,
            Size = checked((ulong)output.Position - stringOffset),
            Alignment = 1,
        };

        DwarfImageWriter.Align(output, bits / 8);
        var symbolOffset = checked((ulong)output.Position);
        var symbolSize = bits == 64 ? 24 : 16;
        var originalLocalCount = originalSymbols?.Info ?? 1;
        var originalSymbolCount = originalSymbols == null ? 1 : checked((uint)(originalSymbols.Size / (ulong)symbolSize));
        var generatedSymbols = debug.Single(s => s.Name == ".symtab").Data;
        using var orderedSymbols = new ElfDebugSymbols(generatedSymbols, bits);
        var addedLocals = orderedSymbols.LocalCount - 1;
        if (originalSymbols != null)
        {
            if (
                originalSymbols.EntrySize != (ulong)symbolSize
                || originalSymbols.Size < (ulong)symbolSize
                || originalSymbols.Size % (ulong)symbolSize != 0
                || originalLocalCount == 0
                || originalLocalCount > originalSymbolCount
            )
                throw new InvalidDataException("Invalid ELF symbol table size.");
            source.CopyTo(output, checked((long)originalSymbols.Offset), checked((long)originalLocalCount * symbolSize));
        }
        else
            writer.Write(new byte[symbolSize]);
        WriteGenerated(orderedSymbols.Locals, symbolSize);
        if (originalSymbols != null)
            source.CopyTo(output, checked((long)originalSymbols.Offset + (long)originalLocalCount * symbolSize), checked((long)originalSymbols.Size - (long)originalLocalCount * symbolSize));
        WriteGenerated(orderedSymbols.Globals, 0);
        headers[symbolIndex] = new Header
        {
            Name = ".symtab",
            NameOffset = headers[symbolIndex].NameOffset,
            Type = 2,
            Offset = symbolOffset,
            Size = checked((ulong)output.Position - symbolOffset),
            Link = checked((uint)stringIndex),
            Info = checked(originalLocalCount + addedLocals),
            Alignment = (ulong)bits / 8,
            EntrySize = (ulong)symbolSize,
        };
        if (originalSymbols != null)
            RemapSymbolReferences(output, bits, headers, symbolIndex, originalSymbolCount, originalLocalCount, addedLocals, orderedSymbols.GlobalCount, source);

        var namesOffset = checked((ulong)output.Position);
        DwarfImageWriter.Copy(names, output);
        headers[(int)nameIndex] = new Header
        {
            Name = ".shstrtab",
            NameOffset = headers[(int)nameIndex].NameOffset,
            Type = 3,
            Offset = namesOffset,
            Size = checked((ulong)names.Stream.Length),
            Alignment = 1,
        };
        DwarfImageWriter.Align(output, bits / 8);
        var newTableOffset = checked((ulong)output.Position);
        headers[0].Size = 0;
        headers[0].Link = 0;
        foreach (var section in headers)
            section.Write(writer, bits);
        if (bits == 64)
            BinaryPrimitives.WriteUInt64LittleEndian(fileHeader.AsSpan(40), newTableOffset);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(fileHeader.AsSpan(32), checked((uint)newTableOffset));
        BinaryPrimitives.WriteUInt16LittleEndian(fileHeader.AsSpan(bits == 64 ? 58 : 46), checked((ushort)entrySize));
        BinaryPrimitives.WriteUInt16LittleEndian(fileHeader.AsSpan(bits == 64 ? 60 : 48), checked((ushort)headers.Count));
        BinaryPrimitives.WriteUInt16LittleEndian(fileHeader.AsSpan(bits == 64 ? 62 : 50), checked((ushort)nameIndex));
        output.Position = 0;
        writer.Write(fileHeader);

        void WriteGenerated(DwarfData symbols, int start)
        {
            symbols.Writer.Flush();
            symbols.Stream.Position = start;
            var record = new byte[symbolSize];
            while (symbols.Stream.Position < symbols.Stream.Length)
            {
                symbols.Stream.ReadExactly(record);
                var stringOffsetInRecord = BinaryPrimitives.ReadUInt32LittleEndian(record);
                BinaryPrimitives.WriteUInt32LittleEndian(record, checked((uint)(stringOffsetInRecord + originalStringSize)));
                var sectionOffset = bits == 64 ? 6 : 14;
                var section = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(sectionOffset));
                if (section == 0 || section >= indices.Length)
                    throw new InvalidDataException("Invalid generated ELF symbol section.");
                BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(sectionOffset), indices[section]);
                writer.Write(record);
            }
        }

        int Add(string name)
        {
            headers.Add(new Header { Name = name, NameOffset = checked((uint)names.String(name)) });
            return headers.Count - 1;
        }

        int GetOrAdd(string name)
        {
            var index = headers.FindIndex(h => h.Name == name);
            return index < 0 ? Add(name) : index;
        }
    }

    private static void RemapSymbolReferences(
        Stream output,
        int bits,
        List<Header> headers,
        int symbolIndex,
        uint originalCount,
        uint originalLocals,
        uint addedLocals,
        uint addedGlobals,
        DwarfImageSource source
    )
    {
        for (var i = 1; i < headers.Count; i++)
        {
            var section = headers[i];
            if (section.Link != symbolIndex)
                continue;
            switch (section.Type)
            {
                case 4:
                case 9:
                    if (addedLocals != 0)
                        Relocations(section);
                    break;
                case 17: // SHT_GROUP: sh_info is its signature symbol index.
                    section.Info = Remap(section.Info);
                    break;
                case 18: // SHT_SYMTAB_SHNDX
                    ParallelTable(section, 4);
                    break;
                case 0x6FFFFFFF: // SHT_GNU_VERSYM
                    ParallelTable(section, 2);
                    break;
                case 5:
                case 0x6FFFFFF6:
                    // Static symbol lookup accelerators are obsolete after adding debug names.
                    if ((section.Flags & 2) != 0)
                        throw new NotSupportedException("Cannot replace a loaded hash table linked to the static ELF symbol table.");
                    headers[i] = new();
                    break;
            }
        }

        uint Remap(uint index)
        {
            if (index >= originalCount)
                throw new InvalidDataException("Invalid ELF symbol index reference.");
            return index < originalLocals ? index : checked(index + addedLocals);
        }

        void Relocations(Header section)
        {
            var entrySize =
                bits == 64
                    ? section.Type == 4
                        ? 24
                        : 16
                    : section.Type == 4
                        ? 12
                        : 8;
            if (section.EntrySize != (ulong)entrySize || section.Size % (ulong)entrySize != 0)
                throw new InvalidDataException("Invalid ELF relocation entry size.");
            var resume = output.Position;
            for (var offset = 0UL; offset < section.Size; )
            {
                var size = checked((int)Math.Min((ulong)entrySize * 4096, section.Size - offset));
                var position = checked((long)(section.Offset + offset));
                var bytes = source.Read(position, size);
                for (var entry = 0; entry < size; entry += entrySize)
                {
                    var info = bytes.AsSpan(entry + bits / 8);
                    if (bits == 64)
                    {
                        var value = BinaryPrimitives.ReadUInt64LittleEndian(info);
                        var symbol = Remap((uint)(value >> 32));
                        BinaryPrimitives.WriteUInt64LittleEndian(info, (ulong)symbol << 32 | (uint)value);
                    }
                    else
                    {
                        var value = BinaryPrimitives.ReadUInt32LittleEndian(info);
                        var symbol = Remap(value >> 8);
                        if (symbol > 0xFFFFFF)
                            throw new InvalidDataException("ELF32 relocation symbol index exceeds 24 bits.");
                        BinaryPrimitives.WriteUInt32LittleEndian(info, symbol << 8 | value & 0xFF);
                    }
                }
                output.Position = position;
                output.Write(bytes);
                offset += (ulong)size;
            }
            output.Position = resume;
        }

        void ParallelTable(Header section, int entrySize)
        {
            if (section.Size != (ulong)originalCount * (ulong)entrySize)
                throw new InvalidDataException("Invalid ELF table indexed by symbols.");
            DwarfImageWriter.Align(output, entrySize);
            var offset = checked((ulong)output.Position);
            var prefix = checked((long)originalLocals * entrySize);
            source.CopyTo(output, checked((long)section.Offset), prefix);
            Zeros((long)addedLocals * entrySize);
            source.CopyTo(output, checked((long)section.Offset + prefix), checked((long)section.Size - prefix));
            Zeros((long)addedGlobals * entrySize);
            section.Offset = offset;
            section.Size = checked((ulong)output.Position - offset);
        }

        void Zeros(long remaining)
        {
            var zeros = new byte[65536];
            while (remaining > 0)
            {
                var count = (int)Math.Min(zeros.Length, remaining);
                output.Write(zeros.AsSpan(0, count));
                remaining -= count;
            }
        }
    }
}
