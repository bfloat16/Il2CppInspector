using System.Text;
using Il2CppInspector.Next.Metadata;
using Il2CppInspector.Plugins;
using static Il2CppInspector.HkrpgRecords;

namespace Il2CppInspector;

internal sealed class HkrpgUsages
{
    internal List<MetadataUsage> Usages { get; } = [];
    private readonly List<AdditionalMetadataUsage> additionalUsages = [];
    internal IEnumerable<AdditionalMetadataUsage> AdditionalUsages => additionalUsages;
    internal int MethodSpecCount { get; }
    internal int MaxTypeIndex { get; }

    internal HkrpgUsages(HkrpgMetadata metadata, HkrpgBinary binary, EventHandler<string> status)
    {
        var header = metadata.Header;
        var global = metadata.Global;
        var usagePairs = new List<(uint Kind, int Source, uint Destination, ulong Address)>();
        var image = binary.Image;
        if (header.UsagePairsOffset < header.UsageListsOffset)
            throw new InvalidDataException("HSR usage pair table precedes usage lists.");
        var span = header.UsagePairsOffset - header.UsageListsOffset;
        if (span < 8 || span % 4 != 0)
            throw new InvalidDataException("Invalid HSR usage-lists span.");
        var lists = metadata.ReadGlobal(header.UsageListsOffset, checked((int)(span / 4)), 4, (d, i) =>
            unchecked(U32(d, 0) + ((uint)((0x8CD81660EE8UL * (ulong)i >> 17) + 0x7AFCE30FUL) ^ 0xEF048154u) - 0x6E8B512Du));
        if (lists[0] != 0 || lists.Zip(lists.Skip(1)).Any(p => p.First > p.Second))
            throw new InvalidDataException("HSR usage lists are not monotonic or do not start at zero.");
        var count = checked((int)lists[^1]);
        CheckRange(global, metadata.Base(header.UsagePairsOffset), checked(count * 8));
        var tableOffsets = new uint[] { 0x50, 0x48, 0, 0x08, 0x20, 0x10, 0x08, 0x40 };
        var bases = tableOffsets.Select((o, i) => i == 2 ? 0UL : image.ReadMappedUInt64(binary.UsageAddress + o)).ToArray();
        var seen = new Dictionary<ulong, (uint Kind, int Source, uint Destination)>();
        for (var i = 0; i < count; i++)
        {
            var v = unchecked((0x87C3UL * (ulong)i ^ 0x5FA3FAD3UL) * 0x334FB2BAUL + 0x0454D10D89E6A02AUL) >> 14;
            var k = unchecked((uint)((v * 0x102533D7UL + 0x0058972D9863A807UL) >> 23));
            var o = checked(metadata.Base(header.UsagePairsOffset) + i * 8);
            var low = unchecked((U32(global, o) ^ 0x6907AB9Au) - k);
            var high = unchecked(U32(global, o + 4) - k - 0x54C5934Bu);
            var kind = high >> 29;
            var source = checked((int)(high & 0x1FFFFFFF));
            if (kind == 2)
                continue;
            var slot = kind is 0 or 7 ? low & 0xFFFFFF : low;
            var address = checked(bases[kind] + (ulong)slot * 8);
            if (!binary.Contains(address, 8))
                throw new InvalidDataException($"HSR usage {i} kind {kind} source {source} resolves outside image: 0x{address:X}.");
            if (seen.TryGetValue(address, out var previous))
            {
                if (previous != (kind, source, low))
                    throw new InvalidDataException($"Conflicting HSR usages at 0x{address:X}.");
                continue;
            }
            seen.Add(address, (kind, source, low));
            usagePairs.Add((kind, source, low, address));
        }
        MethodSpecCount = usagePairs.Where(p => p.Kind == 6).Select(p => p.Source + 1).DefaultIfEmpty().Max();
        MaxTypeIndex = usagePairs.Where(p => p.Kind is 1 or 7).Select(p => p.Source).DefaultIfEmpty().Max();
        var refs = usagePairs.Where(p => p.Kind == 4).Select(p => p.Source + 1).DefaultIfEmpty().Max();
        metadata.Model.FieldRefs = metadata.ReadGlobal(header.FieldRefsOffset, refs, 8, (d, i) =>
        {
            var v = unchecked((ulong)i * 0x28651E907697UL + 0x032D18E4D3C97CDFUL);
            var k = unchecked((uint)(450973069UL * (v >> 20) >> 18)) ^ 0x746F8BFBu;
            return new Il2CppFieldRef
            {
                TypeIndex = unchecked((int)(k ^ (U32(d, 4) - 388656835u))),
                FieldIndex = unchecked((int)(k ^ (U32(d, 0) - 1800319632u))),
            };
        });
        var literalCount = usagePairs.Where(p => p.Kind == 5).Select(p => p.Source + 1).DefaultIfEmpty().Max();
        var literals = new string[literalCount];
        Array.Fill(literals, "");
        foreach (var pair in usagePairs)
        {
            if (pair.Kind == 0)
            {
                var value = metadata.DecodeString((pair.Destination & 0xFF000000) | (uint)pair.Source);
                additionalUsages.Add(new("moraxNativeStrings", -1, pair.Address, $"NativeString_{pair.Address:X}", "const char *") { Value = value, Length = Encoding.UTF8.GetByteCount(value) });
            }
            else if (pair.Kind == 7)
                additionalUsages.Add(new("moraxRuntimeCaches", pair.Source, pair.Address, $"RuntimeType_{pair.Source}_{pair.Destination >> 24}", "void *") { Subtype = pair.Destination >> 24 });
            else
            {
                Usages.Add(new((MetadataUsageType)pair.Kind, pair.Source, pair.Address));
                if (pair.Kind == 5)
                    literals[pair.Source] = DecodeLiteral(metadata, pair.Source);
            }
        }
        metadata.Model.StringLiterals = literals;
        status?.Invoke(this, $"HSR: {count} usage pairs, {Usages.Count} unique standard usages, {additionalUsages.Count} runtime caches/native strings.");
    }

    private static string DecodeLiteral(HkrpgMetadata metadata, int index)
    {
        var header = metadata.Header;
        var global = metadata.Global;
        static uint Key(int i) => unchecked((uint)(0x24085C9AUL * (((ulong)i * 0x32C1CF25BB14UL + 0x001F0FE259CF0538UL) >> 14) >> 8));
        var o = checked(metadata.Base(header.LiteralOffsetsOffset) + index * 4);
        var start = unchecked(U32(global, o) - Key(index));
        var end = unchecked(U32(global, o + 4) - Key(index + 1));
        var length = unchecked(end - start);
        if (length > 0x10000)
            throw new InvalidDataException($"HSR literal {index} has invalid length {length}.");
        var offset = checked((int)((long)header.PayloadOffset + unchecked((int)(header.LiteralDataOffset - 0x08EEB1A7u)) + unchecked((int)(start + 0x3BD9429Bu))));
        var key = unchecked(((ulong)index * 0xDE8C09C836133DBDUL ^ 0x18D025C96EE74E86UL) + 0x2C6833CC6F0A9C48UL);
        return metadata.DecodeBlocks(offset, (int)length, key, 0x464C46540F730312UL, Encoding.UTF8);
    }
}
