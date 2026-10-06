using System.Buffers.Binary;
using System.Text;

namespace Il2CppInspector.Outputs.Pdb;

internal static class PdbStreams
{
    public static void Write(string path, Guid guid, uint age, IReadOnlyList<PdbProcedure> procedures, IReadOnlyList<CvTypeRecord> types, byte[] sections, IReadOnlyList<PdbGlobal> data = null)
    {
        data ??= [];
        using var module = new CvWriter();
        module.U32(4);
        using (var obj = new CvWriter())
        {
            obj.U32(0);
            obj.String("il2cpp.obj");
            module.Bytes(obj.Record(0x1101)); // S_OBJNAME
        }
        using (var compiler = new CvWriter())
        {
            compiler.U32(1); // C++
            compiler.U16(0xD0); // CodeView X64, not the PE machine identifier.
            compiler.Bytes(new byte[16]);
            compiler.String("Il2CppInspector");
            module.Bytes(compiler.Record(0x113C)); // S_COMPILE3
        }
        var offsets = new uint[procedures.Count];
        for (var i = 0; i < procedures.Count; i++)
        {
            var p = procedures[i];
            offsets[i] = (uint)module.Length;
            using var body = new CvWriter();
            body.U32(0);
            body.U32(0);
            body.U32(0);
            body.U32(p.Size);
            body.U32(0);
            body.U32(0);
            body.U32(p.TypeIndex);
            body.U32(p.Offset);
            body.U16(p.Segment);
            body.U8(0);
            body.String(p.Name);
            var record = body.Record(0x1110);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), (uint)(module.Length + record.Length));
            module.Bytes(record);
            module.U16(2);
            module.U16(6);
        }
        var symbolSize = (uint)module.Length;
        module.U32(0);
        var (tpi, hashes) = TypeStreams(types);
        var (symbols, globals, publics) = SymbolStreams(procedures, offsets, data, types);
        byte[][] streams =
        [
            [],
            Info(guid, age),
            tpi,
            Dbi(age, symbolSize, sections),
            EmptyTypes(),
            Names(),
            module.ToArray(),
            hashes,
            globals,
            publics,
            symbols,
            sections,
        ];
        MsfWriter.Write(path, streams);
    }

    private static byte[] Info(Guid guid, uint age)
    {
        using var w = new CvWriter();
        w.U32(20000404);
        w.U32(0);
        w.U32(age);
        w.Bytes(guid.ToByteArray());
        w.U32(7);
        w.String("/names");
        w.U32(1);
        w.U32(2);
        w.U32(1);
        w.U32(1u << (int)(CvWriter.Hash("/names"u8) % 2));
        w.U32(0);
        w.U32(0);
        w.U32(5);
        w.U32(20140508);
        return w.ToArray();
    }

    private static byte[] Names()
    {
        using var w = new CvWriter();
        w.U32(0xEFFEEFFE);
        w.U32(1);
        w.U32(1);
        w.U8(0);
        w.U32(1);
        w.U32(0);
        w.U32(0);
        return w.ToArray();
    }

    private static byte[] EmptyTypes() => TypeStreams([]).Types;

    private static (byte[] Types, byte[] Hashes) TypeStreams(IReadOnlyList<CvTypeRecord> records)
    {
        using var body = new CvWriter();
        using var hashes = new CvWriter();
        using var indices = new CvWriter();
        var last = -8192;
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            if (body.Length - last >= 8192)
            {
                indices.U32(0x1000 + (uint)i);
                indices.U32((uint)body.Length);
                last = body.Length;
            }
            body.Bytes(record.Bytes);
            hashes.U32((record.Name != null && !record.Forward ? CvWriter.Hash(Encoding.UTF8.GetBytes(record.Name)) : CvWriter.RecordHash(record.Bytes)) % 0x3FFFF);
        }
        var hashLength = (uint)hashes.Length;
        hashes.Bytes(indices.ToArray());
        using var header = new CvWriter();
        header.U32(20040203);
        header.U32(56);
        header.U32(0x1000);
        header.U32(0x1000 + (uint)records.Count);
        header.U32((uint)body.Length);
        header.U16(records.Count == 0 ? ushort.MaxValue : (ushort)7);
        header.U16(ushort.MaxValue);
        header.U32(4);
        header.U32(0x3FFFF);
        header.U32(0);
        header.U32(hashLength);
        header.U32(hashLength);
        header.U32((uint)indices.Length);
        header.U32(0);
        header.U32(0);
        header.Bytes(body.ToArray());
        return (header.ToArray(), hashes.ToArray());
    }

    private static (byte[] Contributions, byte[] Map) SectionInfo(byte[] sections)
    {
        if (sections.Length % 40 != 0)
            throw new InvalidDataException("Invalid PE section header size.");
        var count = sections.Length / 40;
        using var contributions = new CvWriter();
        contributions.U32(0xEFFE0000u + 19970605); // DbiSecContribVer60
        using var map = new CvWriter();
        map.U16(checked((ushort)(count + 1)));
        map.U16(checked((ushort)(count + 1)));
        for (var i = 0; i < count; i++)
        {
            var section = sections.AsSpan(i * 40, 40);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(section[8..]);
            var characteristics = BinaryPrimitives.ReadUInt32LittleEndian(section[36..]);
            if (size != 0)
            {
                contributions.U16(checked((ushort)(i + 1)));
                contributions.U16(0);
                contributions.U32(0);
                contributions.U32(size);
                contributions.U32(characteristics);
                contributions.U16(0); // DBI module indices are zero-based.
                contributions.U16(0);
                contributions.U32(0);
                contributions.U32(0);
            }
            ushort flags = 0x100; // IsSelector
            if ((characteristics & 0x40000000) != 0)
                flags |= 1;
            if ((characteristics & 0x80000000) != 0)
                flags |= 2;
            if ((characteristics & 0x20000000) != 0)
                flags |= 4;
            if ((characteristics & 0x20000) == 0)
                flags |= 8;
            WriteMapEntry(map, flags, checked((ushort)(i + 1)), size);
        }
        WriteMapEntry(map, 0x208, checked((ushort)(count + 1)), uint.MaxValue);
        return (contributions.ToArray(), map.ToArray());
    }

    private static void WriteMapEntry(CvWriter map, ushort flags, ushort frame, uint size)
    {
        map.U16(flags);
        map.U16(0);
        map.U16(0);
        map.U16(frame);
        map.U16(ushort.MaxValue);
        map.U16(ushort.MaxValue);
        map.U32(0);
        map.U32(size);
    }

    private static byte[] Dbi(uint age, uint symbols, byte[] sections)
    {
        var (contributions, map) = SectionInfo(sections);
        using var module = new CvWriter();
        module.U32(0);
        if (contributions.Length > 4)
            module.Bytes(contributions.AsSpan(4, 28));
        else
        {
            module.U16(ushort.MaxValue);
            module.U16(0);
            module.U32(0);
            module.U32(uint.MaxValue);
            module.Bytes(new byte[16]);
        }
        module.U16(0);
        module.U16(6);
        module.U32(symbols);
        module.U32(0);
        module.U32(0);
        module.U16(0);
        module.U16(0);
        module.U32(0);
        module.U32(0);
        module.U32(0);
        module.String("il2cpp");
        module.String("il2cpp");
        module.Align();
        var names = Names();
        using var w = new CvWriter();
        w.U32(uint.MaxValue);
        w.U32(19990903);
        w.U32(age);
        w.U16(8);
        w.U16(36363);
        w.U16(9);
        w.U16(0);
        w.U16(10);
        w.U16(0);
        w.U32((uint)module.Length);
        w.U32((uint)contributions.Length);
        w.U32((uint)map.Length);
        w.U32(8);
        w.U32(0);
        w.U32(0);
        w.U32(22);
        w.U32((uint)names.Length);
        w.U16(0);
        w.U16(0x8664);
        w.U32(0);
        w.Bytes(module.ToArray());
        w.Bytes(contributions);
        w.Bytes(map);
        w.U16(1);
        w.U16(0);
        w.U16(0);
        w.U16(0);
        w.Bytes(names);
        for (var i = 0; i < 11; i++)
            w.U16(i == 5 ? (ushort)11 : ushort.MaxValue);
        return w.ToArray();
    }

    private sealed record HashEntry(uint Offset, byte[] Name);

    private static byte[] HashSymbols(List<HashEntry> entries)
    {
        var buckets = new List<HashEntry>[4096];
        foreach (var entry in entries)
        {
            var index = CvWriter.Hash(entry.Name) % 4096;
            (buckets[index] ??= []).Add(entry);
        }
        using var records = new CvWriter();
        using var offsets = new CvWriter();
        var bitmap = new uint[129];
        var count = 0u;
        for (var i = 0; i < buckets.Length; i++)
        {
            if (buckets[i] == null)
                continue;
            bitmap[i / 32] |= 1u << (i % 32);
            offsets.U32(count * 12);
            buckets[i]
                .Sort(
                    (a, b) =>
                    {
                        var comparison = a.Name.Length.CompareTo(b.Name.Length);
                        if (comparison == 0)
                            comparison = a.Name.AsSpan().SequenceCompareTo(b.Name);
                        return comparison != 0 ? comparison : a.Offset.CompareTo(b.Offset);
                    }
                );
            foreach (var entry in buckets[i])
            {
                records.U32(entry.Offset + 1);
                records.U32(1);
                count++;
            }
        }
        using var output = new CvWriter();
        output.U32(uint.MaxValue);
        output.U32(0xF12F091A);
        output.U32((uint)records.Length);
        output.U32((uint)(bitmap.Length * 4 + offsets.Length));
        output.Bytes(records.ToArray());
        foreach (var word in bitmap)
            output.U32(word);
        output.Bytes(offsets.ToArray());
        return output.ToArray();
    }

    private static byte[] DataSymbol(PdbGlobal variable)
    {
        using var record = new CvWriter();
        record.U32(variable.TypeIndex);
        record.U32(variable.Offset);
        record.U16(variable.Segment);
        record.String(variable.Name);
        return record.Record(0x110D); // S_GDATA32, as emitted by LLVM's emitDebugInfoForGlobal.
    }

    private static (byte[] Symbols, byte[] Globals, byte[] Publics) SymbolStreams(
        IReadOnlyList<PdbProcedure> procedures,
        uint[] moduleOffsets,
        IReadOnlyList<PdbGlobal> data,
        IReadOnlyList<CvTypeRecord> types
    )
    {
        using var symbols = new CvWriter();
        var publics = new List<HashEntry>(procedures.Count);
        var globals = new List<HashEntry>(procedures.Count + data.Count);
        foreach (var p in procedures)
        {
            publics.Add(new((uint)symbols.Length, Encoding.UTF8.GetBytes(p.Name)));
            using var record = new CvWriter();
            record.U32(2);
            record.U32(p.Offset);
            record.U16(p.Segment);
            record.String(p.Name);
            symbols.Bytes(record.Record(0x110E));
        }
        for (var i = 0; i < procedures.Count; i++)
        {
            globals.Add(new((uint)symbols.Length, publics[i].Name));
            using var record = new CvWriter();
            record.U32(0);
            record.U32(moduleOffsets[i]);
            record.U16(1);
            record.String(procedures[i].Name);
            symbols.Bytes(record.Record(0x1125));
        }
        foreach (var variable in data)
        {
            globals.Add(new((uint)symbols.Length, Encoding.UTF8.GetBytes(variable.Name)));
            symbols.Bytes(DataSymbol(variable));
        }
        for (var i = 0; i < types.Count; i++)
        {
            var type = types[i];
            if (type.Name == null || type.Forward)
                continue;
            globals.Add(new((uint)symbols.Length, Encoding.UTF8.GetBytes(type.Name)));
            using var record = new CvWriter();
            record.U32(checked(0x1000 + (uint)i));
            record.String(type.Name);
            symbols.Bytes(record.Record(0x1108)); // S_UDT references the complete TPI definition.
        }
        var publicHash = HashSymbols(publics);
        using var output = new CvWriter();
        output.U32((uint)publicHash.Length);
        output.U32(checked((uint)procedures.Count * 4));
        output.U32(0);
        output.U32(0);
        output.U16(0);
        output.U16(0);
        output.U32(0);
        output.U32(0);
        output.Bytes(publicHash);
        foreach (var i in Enumerable.Range(0, procedures.Count).OrderBy(i => procedures[i].Segment).ThenBy(i => procedures[i].Offset))
            output.U32(publics[i].Offset);
        return (symbols.ToArray(), HashSymbols(globals), output.ToArray());
    }
}
