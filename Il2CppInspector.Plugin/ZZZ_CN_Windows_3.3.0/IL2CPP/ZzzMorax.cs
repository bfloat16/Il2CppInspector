using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInspector.Next;
using Il2CppInspector.Next.Metadata;
using static Il2CppInspector.MoraxReader;

namespace Il2CppInspector;

/// <summary>Static C# adapter for the IDA-verified ZZZ 3.3.0 Windows MORAX build.</summary>
internal sealed partial class ZzzMorax : MoraxAdapter
{
    public override string StorageBase(uint tag) =>
        tag switch
        {
            0 => "klass->static_fields",
            1 => "thread static storage",
            2 => "MetadataRegistration->globalStaticStorage (+0x88)",
            4 => "*MetadataRegistration->globalStaticStorageSlot (+0x68)",
            _ => "unknown",
        };

    public override int VTableSlotSize => 8;
    private const int Payload = 0x198;
    private readonly byte[] global;
    private readonly byte[] startup;
    private readonly Dictionary<int, int> slots;
    internal readonly Metadata Metadata;

    private int Slot(int o) => slots[o];

    private int Base(int o) => checked(Payload + Slot(o));

    internal int DefaultBlobOffset => Base(0xB4);

    private ZzzMorax(IFileFormatStream image, byte[] globalData, byte[] startupData, EventHandler<string> status, Plugins.GamePlugin plugin)
        : base(image, plugin)
    {
        global = globalData;
        startup = startupData;
        if (!IsMhy(global))
        {
            throw new InvalidDataException("Missing MHY metadata container signature.");
        }

        var headerAddress = DiscoverRegistration(t => HasMhyHeader(image, t));
        // +198 is the file payload base, not a proven embedded-header size. Read only verified slots.
        slots = DecodeSlots(image.ReadMappedBytes(headerAddress, HeaderCiphers.Max(c => c.Offset) + sizeof(uint)), HeaderCiphers);
        // MHY is shared by several games. Reject unknown layouts before allocating table arrays.
        if (
            Slot(0xA8) / 80 != 96788
            || Slot(0x178) / 30 != 854677
            || Slot(0x188) / 40 != 170
            || (image.ReadMappedUInt32(MetadataRegistrationAddress + 0x40) ^ 0x3745AAFC) != 970056
        )
        {
            throw new NotSupportedException("This MORAX build is not the supported ZenlessZoneZero 3.3.0 Windows layout. Keys vary per build.");
        }

        Metadata = Metadata.CreateForPlugin(global, this, MetadataVersions.V245, status);
        status?.Invoke(this, $"Decoding {plugin.GameId} metadata in C#");
        ReadMetadata();
    }

    internal static Il2CppInspector Load(
        IFileFormatStream image,
        byte[] global,
        byte[] startup,
        EventHandler<string> status,
        Plugins.GamePlugin plugin
    )
    {
        var adapter = new ZzzMorax(image, global, startup, status, plugin);
        var binary = new Il2CppBinaryX64(image, status);
        adapter.ReadBinary(binary);
        return new Il2CppInspector(binary, adapter.Metadata);
    }

    private void ReadMetadata()
    {
        Metadata.Images = Records(startup, Slot(0xD4), Slot(0x188) / 40, 40, Il2CppImageDefinitionReader.FromZzz);
        var typeCount = 0;
        foreach (var image in Metadata.Images)
        {
            if (image.TypeStart != typeCount)
            {
                throw new InvalidDataException("ZZZ image type ranges are not contiguous.");
            }

            typeCount = checked(typeCount + (int)image.TypeCount);
        }
        var types = Records(global, Base(0x30), Slot(0xA8) / 80, 80, (d, _) => Il2CppTypeDefinitionReader.FromZzz(d)).ToArray();
        if (
            typeCount > types.Length
            || types
                .Skip(typeCount)
                .Any(t => t.ByValTypeIndex != -1 || t.ByRefTypeIndex != -1 || t.Token != 0x02000000 || (uint)t.NameIndex >> 24 != 0)
        )
        {
            throw new InvalidDataException("Unexpected ZZZ type records outside the image ranges.");
        }
        // The physical table ends with non-type sentinels. Preserve every real definition's index.
        Array.Resize(ref types, typeCount);
        Metadata.Methods = Records(global, Base(0x50), Slot(0x178) / 30, 30, (d, _) => Il2CppMethodDefinitionReader.FromZzz(d));
        var fieldCount = types.Max(t => t.FieldCount == 0 ? 0 : checked((int)t.FieldIndex + t.FieldCount));
        var propertyCount = types.Max(t => t.PropertyCount == 0 ? 0 : checked((int)t.PropertyIndex + t.PropertyCount));
        var eventCount = types.Max(t => t.EventCount == 0 ? 0 : checked((int)t.EventIndex + t.EventCount));
        Metadata.Fields = Records(global, Base(0xEC), fieldCount, 12, Il2CppFieldDefinitionReader.FromZzz);
        Metadata.Properties = Records(global, Base(0xB0), propertyCount, 14, Il2CppPropertyDefinitionReader.FromZzz);
        Metadata.Events = Records(global, Base(0x48), eventCount, 18, Il2CppEventDefinitionReader.FromZzz);
        var paramCount = Metadata.Methods.Max(m =>
            m.ParameterCount == 0 || m.ParameterStart < 0 ? 0 : checked((int)m.ParameterStart + m.ParameterCount)
        );
        Metadata.Params = Records(global, Base(0xD0), paramCount, 12, Il2CppParameterDefinitionReader.FromZzz);
        var nestedCount = types.Max(t => t.NestedTypeCount == 0 ? 0 : (int)t.NestedTypeIndex + t.NestedTypeCount);
        Metadata.NestedTypeIndices = Records(global, Base(0x64), nestedCount, 4, (d, _) => I32(d, 0));
        var interfaceCount = types.Max(t => t.InterfacesCount == 0 ? 0 : (int)t.InterfacesIndex + t.InterfacesCount);
        Metadata.InterfaceUsageIndices = Records(global, Base(0x168), interfaceCount, 4, (d, _) => new TypeIndex(I32(d, 0)));
        var interfaceOffsetsCount = types.Max(t => t.InterfaceOffsetsCount == 0 ? 0 : (int)t.InterfaceOffsetsStart + t.InterfaceOffsetsCount);
        Metadata.InterfaceOffsets = Records(global, Base(0xF0), interfaceOffsetsCount, 6, Il2CppInterfaceOffsetPairReader.FromZzz);
        var vtableCount = types.Where(t => t.VTableIndex >= 0 && t.VTableIndex < 0x08000000).Max(t => t.VTableIndex + t.VTableCount);
        Metadata.VTableMethodIndices = Records(global, Base(0x3C), vtableCount, 4, (d, _) => U32(d, 0));
        for (var i = 0; i < types.Length; i++)
        {
            var t = types[i];
            for (var n = 0; n < t.NestedTypeCount; n++)
            {
                var child = Metadata.NestedTypeIndices[t.NestedTypeIndex + n];
                if ((uint)child >= types.Length)
                {
                    throw new InvalidDataException("ZZZ nested type index out of range.");
                }

                types[child].DeclaringTypeIndex = t.ByValTypeIndex;
            }
            if (t.Bitfield.EnumType)
            {
                for (var f = 0; f < t.FieldCount; f++)
                {
                    var field = Metadata.Fields[t.FieldIndex + f];
                    if (DecodeString((uint)field.NameIndex) == "value__")
                    {
                        types[i].ElementTypeIndex = field.TypeIndex;
                    }
                }
            }
        }
        Metadata.Types = ImmutableCollectionsMarshal.AsImmutableArray(types);
        Metadata.Assemblies = Records(startup, Slot(0x120), Slot(0xA0) / 16, 16, ReadAssembly);
        var containers = types.Select(t => (int)t.GenericContainerIndex).Concat(Metadata.Methods.Select(m => (int)m.GenericContainerIndex)).Max() + 1;
        Metadata.GenericContainers = Records(global, Base(0x98), containers, 16, Il2CppGenericContainerReader.FromZzz);
        var parameters = Metadata.GenericContainers.Max(c => (int)c.GenericParameterStart + c.TypeArgc);
        Metadata.GenericParameters = Records(global, Base(0xBC), parameters, 14, (d, _) => Il2CppGenericParameterReader.FromZzz(d));
        var constraints = Metadata.GenericParameters.Max(p => p.ConstraintsCount == 0 ? 0 : p.ConstraintsStart + p.ConstraintsCount);
        Metadata.GenericConstraintIndices = Records(global, Base(0x80), constraints, 4, (d, _) => new TypeIndex(I32(d, 0)));
        Metadata.FieldDefaultValues = Records(global, Base(0x114), Slot(0x18) / 12, 12, (d, _) => Il2CppFieldDefaultValueReader.FromZzz(d));
        // The loader computes the end with unsigned size / 12; the +1 remainder is not another record.
        Metadata.ParameterDefaultValues = Records(global, Base(0x130), Slot(0xA4) / 12, 12, (d, _) => Il2CppParameterDefaultValueReader.FromZzz(d));
        var attributes = checked((int)Metadata.Images.Max(i => (uint)i.CustomAttributeStart + i.CustomAttributeCount));
        Metadata.AttributeTypeRanges = Records(global, Base(0x20), attributes, 8, (d, _) => Il2CppCustomAttributeTypeRangeReader.FromZzz(d));
        var attributeTypes = Metadata.AttributeTypeRanges.Max(a => a.Start + a.Count);
        Metadata.AttributeTypeIndices = Records(global, Base(0x08), attributeTypes, 4, (d, _) => I32(d, 0));
        Metadata.AttributeDataRanges = [];
        Metadata.MetadataUsageLists = [];
        Metadata.MetadataUsagePairs = [];
        Metadata.TypeInlineArrays = [];
        foreach (var t in types)
        {
            Intern(t.NameIndex);
            Intern(t.NamespaceIndex);
        }
        foreach (var f in Metadata.Fields)
        {
            Intern(f.NameIndex);
        }

        foreach (var m in Metadata.Methods)
        {
            Intern(m.NameIndex);
        }

        foreach (var p in Metadata.Params)
        {
            Intern(p.NameIndex);
        }

        foreach (var p in Metadata.Properties)
        {
            Intern(p.NameIndex);
        }

        foreach (var e in Metadata.Events)
        {
            Intern(e.NameIndex);
        }

        foreach (var p in Metadata.GenericParameters)
        {
            Intern(p.NameIndex);
        }

        foreach (var i in Metadata.Images)
        {
            Intern(i.NameIndex);
        }

        foreach (var a in Metadata.Assemblies)
        {
            Intern(a.Aname.NameIndex);
            Intern(a.Aname.CultureIndex);
        }
        ReadLiterals();
    }

    private void Intern(int index)
    {
        if (!Metadata.Strings.ContainsKey(index))
        {
            Metadata.Strings.Add(index, DecodeString(unchecked((uint)index)));
        }
    }

    private string DecodeString(uint index)
    {
        if (index == uint.MaxValue || index >> 24 == 0)
        {
            return "";
        }

        var offset = (int)(index & 0xFFFFFF);
        var key =
            unchecked(
                (0x7F38C334044047A6UL * ((0x256091B36536B76BUL * (ulong)(uint)offset) ^ 0x7EB3F0E16BDA6E1AUL) + 0x65F4151A13728A19UL)
                ^ 0x1B17927037F0B4C7UL
            ) + 0x28ABE6A6728B5EA4UL;
        return DecodeBlocks(global, Base(0x18C) + offset, (int)(index >> 24), key, 0x7C4607E0228F1B5FUL);
    }

    private void ReadLiterals()
    {
        var count = checked((int)unchecked(Image.ReadMappedUInt32(UsageAddress + 0x30) ^ 0x7B42045Cu));
        var offsets = Base(0x134);
        uint Offset(int i) => unchecked((((0x9BD2A0F8u * (uint)i) ^ 0x231AF17Bu) + 0x45834549u) ^ (U32(global, offsets + i * 4) - 0x4255DF16u));
        CheckRange(global, offsets, checked((count + 1) * 4));
        var literals = new string[count];
        for (var i = 0; i < count; i++)
        {
            var start = checked((int)Offset(i));
            var length = checked((int)Offset(i + 1) - start);
            if (length < 0)
            {
                throw new InvalidDataException("ZZZ literal offsets are not monotonic.");
            }

            var key = unchecked(((0xBD34252137864B4FUL * (ulong)i - 0x59E646F18F7029AAUL) ^ 0x7CB87E2907944BE2UL) + 0x55D2E70F0B2FA3B0UL);
            literals[i] = DecodeBlocks(global, Base(0xB8) + start, length, key, 0x1EE3292D1A24C6D7UL);
        }
        Metadata.StringLiterals = literals;
    }

    private Il2CppAssemblyDefinition ReadAssembly(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked((uint)(((0x7C2CDDADUL * ((0x494020E6019EUL * (uint)i) >> 0x10) + 0x558B2553D0EE6UL) >> 0x13)));
        var nameData = startup.AsSpan(Slot(0x74) + i * 44, 44);
        var nk = unchecked((uint)((((0x1F304A1B12E8UL * (uint)i) >> 0x16) ^ 0x756D089CUL) + 0x1E0C1412UL));
        var name = new Il2CppAssemblyNameDefinition
        {
            NameIndex = unchecked((int)((U32(nameData, 0x14) - 0x1D8CADA5u) ^ nk)),
            CultureIndex = unchecked((int)(U32(nameData, 0x08) ^ nk ^ 0x693D608Du)),
            // The runtime name has no full public-key pointer.
            PublicKeyIndex = -1,
            HashAlg = (AssemblyHashAlgorithm)unchecked(U32(nameData, 0x0C) ^ nk ^ 0x2FE86890u),
            HashLen = unchecked((int)(U32(nameData, 0x1C) ^ nk ^ 0x3180F229u)),
            Build = unchecked((int)((U32(nameData, 0x18) - 0x0A394E27u) ^ nk)),
            Minor = unchecked((int)((U32(nameData, 0x04) - 0x6DC03092u) ^ nk)),
            Major = unchecked((int)(U32(nameData, 0x00) ^ nk ^ 0x29D2A5D4u)),
            Revision = unchecked((int)((U32(nameData, 0x10) - 0x2C0E17A7u) ^ nk)),
            Flags = (AssemblyNameFlags)unchecked(U32(nameData, 0x20) ^ nk ^ 0x6AC84C5Eu ^ 0x0802502Au),
        };
        nameData.Slice(0x24, 8).CopyTo(name.PublicKeyToken);
        return new()
        {
            Aname = name,
            ReferencedAssemblyStart = unchecked((int)(U32(d, 4) ^ k ^ 0x7B0C44BEu)),
            Token = unchecked((U32(d, 12) - 0x38567D76u) ^ k),
            ImageIndex = unchecked((int)((U32(d, 8) - 0x4D2FB852u) ^ k)),
            ReferencedAssemblyCount = unchecked((int)((U32(d, 0) - 0x5BFD62A3u) ^ k)),
        };
    }

    // Header slots used by the 3.3.0 metadata and binary loaders.
    private static readonly (int Offset, int Kind, uint Key)[] HeaderCiphers =
    [
        (0x008, 0, 0x1CA36193),
        (0x014, 0, 0x134F1C0D),
        (0x018, 2, 0x6E1A3D3D),
        (0x020, 2, 0x47A1738F),
        (0x028, 0, 0x329B3CA8),
        (0x030, 2, 0x14F83E8A),
        (0x03C, 2, 0x3947B535),
        (0x048, 0, 0x6051E9A3),
        (0x04C, 0, 0x1AA7E489),
        (0x050, 2, 0x6EBA82EA),
        (0x064, 0, 0x1818421B),
        (0x074, 2, 0x51718246),
        (0x080, 0, 0x11E13AF3),
        (0x084, 2, 0x56AE425C),
        (0x094, 2, 0x3CF22415),
        (0x098, 2, 0x361FF75F),
        (0x0A0, 2, 0x242D92A2),
        (0x0A4, 0, 0x019B1D58),
        (0x0A8, 2, 0x1FF1F4AC),
        (0x0AC, 0, 0x54E63BD3),
        (0x0B0, 2, 0x08BD3E67),
        (0x0B4, 2, 0x7937F56D),
        (0x0B8, 2, 0x21132B10),
        (0x0BC, 0, 0x53E1E515),
        (0x0C0, 5, 0x1951D03C),
        (0x0C4, 0, 0x62795943),
        (0x0D0, 0, 0x414CEC86),
        (0x0D4, 2, 0x0AB7261A),
        (0x0E0, 0, 0x51DF2F15),
        (0x0EC, 2, 0x2C235161),
        (0x0F0, 2, 0x0EEBE2A4),
        (0x108, 0, 0x5C64C7E8),
        (0x10C, 2, 0x7989DEBA),
        (0x114, 2, 0x1B806B50),
        (0x120, 0, 0x51E82004),
        (0x130, 2, 0x4B23F6B6),
        (0x134, 2, 0x1C31D2EB),
        (0x13C, 0, 0x68C808ED),
        (0x14C, 0, 0x416F5FE4),
        (0x164, 0, 0x060DBF90),
        (0x168, 0, 0x75C67B46),
        (0x16C, 2, 0x109098B1),
        (0x170, 2, 0x25F2B63B),
        (0x178, 2, 0x0DAAEB06),
        (0x188, 0, 0x30F23F80),
        (0x18C, 0, 0x36371F3C),
        (0x190, 2, 0x3F1F3892),
    ];
}
