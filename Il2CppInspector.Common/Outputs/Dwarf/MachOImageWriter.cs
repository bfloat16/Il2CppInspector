using System.Buffers.Binary;
using System.Text;

namespace Il2CppInspector.Outputs.Dwarf;

internal static class MachOImageWriter
{
    public static void Write(Stream output, int bits, IReadOnlyList<ElfDebugSection> sections, DwarfImageSource source)
    {
        var headerSize = bits == 64 ? 32 : 28;
        var segmentSize = bits == 64 ? 72 : 56;
        var sectionSize = bits == 64 ? 80 : 68;
        var segmentKind = bits == 64 ? 0x19u : 1u;
        var header = source.Read(0, headerSize);
        var commandCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
        var commandSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20));
        var originalCommands = source.Read(headerSize, checked((int)commandSize));
        var commands = new List<byte[]>();
        var sectionIndices = new Dictionary<int, int> { [0] = 0 };
        var originalSectionIndex = 0;
        var newSectionIndex = 0;
        long firstData = source.Length;
        ulong virtualEnd = 0;
        byte[] symbolCommand = null;
        var cursor = 0;
        for (var i = 0u; i < commandCount; i++)
        {
            if (cursor > originalCommands.Length - 8)
                throw new InvalidDataException("Invalid Mach-O load command range.");
            var kind = BinaryPrimitives.ReadUInt32LittleEndian(originalCommands.AsSpan(cursor));
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(originalCommands.AsSpan(cursor + 4)));
            if (size < 8 || size > originalCommands.Length - cursor || size % (bits / 8) != 0)
                throw new InvalidDataException("Invalid Mach-O load command size.");
            var command = originalCommands.AsSpan(cursor, size).ToArray();
            cursor += size;
            if (kind == segmentKind)
            {
                if (size < segmentSize)
                    throw new InvalidDataException("Invalid Mach-O segment command.");
                var dwarf = Name(command.AsSpan(8, 16)) == "__DWARF";
                var count = BinaryPrimitives.ReadUInt32LittleEndian(command.AsSpan(bits == 64 ? 64 : 48));
                if (count > (size - segmentSize) / sectionSize)
                    throw new InvalidDataException("Invalid Mach-O segment section count.");
                if (!dwarf)
                    virtualEnd = Math.Max(virtualEnd, checked(Word(command, 24) + Word(command, 24 + bits / 8)));
                var fileOffset = Word(command, 24 + bits / 8 * 2);
                var fileSize = Word(command, 24 + bits / 8 * 3);
                if (fileSize != 0 && fileOffset != 0)
                    firstData = Math.Min(firstData, checked((long)fileOffset));
                for (var s = 0u; s < count; s++)
                {
                    var offset = checked(segmentSize + (int)s * sectionSize);
                    var section = command.AsSpan(offset, sectionSize);
                    var flags = BinaryPrimitives.ReadUInt32LittleEndian(section[(bits == 64 ? 64 : 56)..]);
                    var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(section[(bits == 64 ? 48 : 40)..]);
                    var dataSize = Word(command, offset + 32 + bits / 8);
                    if ((flags & 0xFF) is not (1 or 0xC or 0x12) && dataSize != 0 && dataOffset != 0)
                        firstData = Math.Min(firstData, dataOffset);
                    var relocations = BinaryPrimitives.ReadUInt32LittleEndian(section[(bits == 64 ? 56 : 48)..]);
                    if (relocations != 0)
                        firstData = Math.Min(firstData, relocations);
                    sectionIndices.Add(++originalSectionIndex, dwarf ? 0 : ++newSectionIndex);
                }
                if (dwarf)
                    continue;
            }
            if (kind == 2)
            {
                if (size != 24)
                    throw new InvalidDataException("Invalid Mach-O symbol command.");
                symbolCommand = command;
                foreach (var offset in new[] { 8, 16 })
                {
                    var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(command.AsSpan(offset));
                    if (dataOffset != 0)
                        firstData = Math.Min(firstData, dataOffset);
                }
            }
            // Changing load commands invalidates the input's code signature.
            if (kind is 0x1D or 0x2B)
                continue;
            commands.Add(command);
        }
        if (cursor != originalCommands.Length)
            throw new InvalidDataException("Mach-O load commands do not match sizeofcmds.");

        var debug = sections.Where(s => s.Name.StartsWith(".debug_", StringComparison.Ordinal)).ToArray();
        var debugCommandSize = checked(segmentSize + debug.Length * sectionSize);
        var newCommandSize = checked(commands.Sum(c => c.Length) + debugCommandSize);
        if (headerSize + (long)newCommandSize > firstData)
            throw new NotSupportedException("Mach-O has insufficient header padding to embed DWARF without moving its loaded contents.");
        var pageSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) == 0x100000C ? 16384 : 4096;
        source.CopyTo(output);
        DwarfImageWriter.Align(output, pageSize);
        var debugOffset = checked((ulong)output.Position);
        var debugAddress = checked((virtualEnd + (ulong)pageSize - 1) / (ulong)pageSize * (ulong)pageSize);
        var offsets = new uint[debug.Length];
        for (var i = 0; i < debug.Length; i++)
        {
            DwarfImageWriter.Align(output, bits / 8);
            offsets[i] = checked((uint)output.Position);
            DwarfImageWriter.Copy(debug[i].Data, output);
        }
        var debugSize = checked((ulong)output.Position - debugOffset);
        using var commandStream = new MemoryStream();
        using (var writer = new BinaryWriter(commandStream, Encoding.UTF8, true))
        {
            writer.Write(segmentKind);
            writer.Write(checked((uint)debugCommandSize));
            FixedName(writer, "__DWARF");
            WriteWord(debugAddress);
            WriteWord(checked((debugSize + (ulong)pageSize - 1) / (ulong)pageSize * (ulong)pageSize));
            WriteWord(debugOffset);
            WriteWord(debugSize);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(checked((uint)debug.Length));
            writer.Write(0u);
            for (var i = 0; i < debug.Length; i++)
            {
                FixedName(writer, "__" + debug[i].Name[1..]);
                FixedName(writer, "__DWARF");
                WriteWord(checked(debugAddress + offsets[i] - debugOffset));
                WriteWord(checked((ulong)debug[i].Data.Stream.Length));
                writer.Write(offsets[i]);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(0x02000000u); // S_ATTR_DEBUG
                writer.Write(0u);
                writer.Write(0u);
                if (bits == 64)
                    writer.Write(0u);
            }

            void WriteWord(ulong value)
            {
                if (bits == 64)
                    writer.Write(value);
                else
                    writer.Write(checked((uint)value));
            }
        }
        commands.Add(commandStream.ToArray());
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), checked((uint)commands.Count));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), checked((uint)newCommandSize));
        output.Position = 0;
        output.Write(header);
        foreach (var command in commands)
            output.Write(command);
        if (newCommandSize < originalCommands.Length)
            output.Write(new byte[originalCommands.Length - newCommandSize]);

        if (symbolCommand != null && sectionIndices.Any(p => p.Key != p.Value))
            RemapSymbols(output, bits, symbolCommand, sectionIndices, source);

        ulong Word(byte[] bytes, int offset) => bits == 64 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset)) : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    }

    private static void RemapSymbols(Stream output, int bits, byte[] command, Dictionary<int, int> indices, DwarfImageSource source)
    {
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(command.AsSpan(8));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(command.AsSpan(12));
        var entrySize = bits == 64 ? 16 : 12;
        for (var first = 0u; first < count; )
        {
            var entries = Math.Min(count - first, 4096u);
            var position = checked((long)offset + (long)first * entrySize);
            var bytes = source.Read(position, checked((int)entries * entrySize));
            for (var i = 0; i < entries; i++)
            {
                var sectionOffset = checked((int)i * entrySize + 5);
                var section = bytes[sectionOffset];
                if (!indices.TryGetValue(section, out var newIndex))
                    throw new InvalidDataException("Invalid Mach-O symbol section index.");
                bytes[sectionOffset] = checked((byte)newIndex);
            }
            output.Position = position;
            output.Write(bytes);
            first += entries;
        }
    }

    private static string Name(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? bytes : bytes[..end]);
    }

    private static void FixedName(BinaryWriter writer, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        if (bytes.Length > 16)
            throw new InvalidDataException("Mach-O section name exceeds sixteen bytes.");
        writer.Write(bytes);
        writer.Write(new byte[16 - bytes.Length]);
    }
}
