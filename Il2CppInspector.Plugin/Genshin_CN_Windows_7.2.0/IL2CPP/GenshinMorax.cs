using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInspector.Next;
using Il2CppInspector.Next.Metadata;
using VersionedSerialization;
using static Il2CppInspector.MoraxReader;

namespace Il2CppInspector;

/// <summary>Metadata and binary adapter for the Genshin 7.2.0 Windows MORAX build.</summary>
internal sealed partial class GenshinMorax : MoraxAdapter
{
    public override string StorageBase(uint tag) =>
        tag switch
        {
            0 => "klass->static_fields (+0x38)",
            1 => "thread static storage",
            2 => "MetadataRegistration->globalStaticStorage (+0x30)",
            4 => "*MetadataRegistration->globalStaticStorageSlot (+0x90)",
            _ => "unknown",
        };

    public override int VTableSlotSize => 8;
    private const int Payload = 0x210;
    private const int DefaultDataSlot = 0x1CC;
    private readonly byte[] global;
    private readonly byte[] startup;
    private readonly Dictionary<int, int> slots;
    internal readonly Metadata Metadata;

    // 24.1 selects global method/Invoker arrays and image-scoped CA tokens.
    // Every physical record is read explicitly; this is not the original player version.
    private static readonly StructVersion NominalMetadataVersion = MetadataVersions.V241;

    private int Slot(int o) => slots[o];

    private int Base(int o) => checked(Payload + Slot(o));

    // Physical table boundaries avoid the padded size slots in this build.
    private int TableCount(int startSlot, int endSlot, int stride)
    {
        var length = checked(Slot(endSlot) - Slot(startSlot));
        if (length < 0)
            throw new InvalidDataException("Genshin table boundaries are reversed.");
        return length / stride;
    }

    private GenshinMorax(IFileFormatStream image, byte[] globalData, byte[] startupData, EventHandler<string> status, Plugins.GamePlugin plugin)
        : base(image, plugin)
    {
        global = globalData;
        startup = startupData;
        // The registration block is at this build's verified RVA 0x2F8450.
        var headerVa = DiscoverRegistration(t => t[0] == image.ImageBase + 0x2F8450 && HasMhyHeader(image, t));
        if (UsageAddress != MetadataRegistrationAddress + 0x98)
            throw new InvalidDataException("Genshin registration/header mismatch.");
        // The effective header is the 0x210-byte "MHY\0" block embedded in YuanShen.exe,
        // not the decoy block at the start of global-metadata.dat.
        slots = DecodeSlots(image.ReadMappedBytes(headerVa, HeaderCiphers.Max(c => c.Offset) + sizeof(uint)), HeaderCiphers);
        // MHY is shared by several games. Reject unknown layouts before allocating table arrays.
        ValidateLayout();

        Metadata = Metadata.CreateForPlugin(global, this, NominalMetadataVersion, status);
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
        var adapter = new GenshinMorax(image, global, startup, status, plugin);
        var binary = new Il2CppBinaryX64(image, status);
        adapter.ReadBinary(binary);
        return new Il2CppInspector(binary, adapter.Metadata);
    }

    // Verified payload-relative table offsets. A mismatch means a different MORAX build whose slot keys and table layout do not apply.
    private void ValidateLayout()
    {
        if (
            Slot(0x64) != 0xA0CE70
            || Slot(0xB0) != 0x1531FE0
            || Slot(0x1C) / 26 != 741021
            || TableCount(0x64, 0x1A0, 70) != 89748
            || Slot(0x1EC) != 0x3A7D34
            || Slot(0x1C0) / 40 != 75
            || startup.Length != 4116148
        )
            throw new NotSupportedException("Unsupported Genshin MORAX build: permutations and keys vary per build.");
    }

    private void ReadMetadata()
    {
        Metadata.Images = Records(startup, Slot(0x1EC), Slot(0x1C0) / 40, 40, GenshinRecords.Image);
        var typeCount = 0;
        foreach (var image in Metadata.Images)
        {
            if (image.TypeStart != typeCount)
                throw new InvalidDataException("Image type ranges are not contiguous.");
            typeCount = checked(typeCount + (int)image.TypeCount);
        }
        if (typeCount != 89746)
            throw new InvalidDataException("Unexpected real type count.");
        var types = Records(global, Base(0x64), typeCount, 70, GenshinRecords.Type);
        Metadata.Types = types;
        var methods = Records(global, Base(0xB0), Slot(0x1C) / 26, 26, GenshinRecords.Method).ToArray();
        for (var i = 0; i < methods.Length; i++)
            methods[i].InvokerIndex = I32(global, Base(0xC4) + i * 4);
        Metadata.Methods = ImmutableCollectionsMarshal.AsImmutableArray(methods);
        var fields = types.Max(t => t.FieldCount == 0 ? 0 : checked((int)t.FieldIndex + t.FieldCount));
        var properties = types.Max(t => t.PropertyCount == 0 ? 0 : checked((int)t.PropertyIndex + t.PropertyCount));
        var events = types.Max(t => t.EventCount == 0 ? 0 : checked((int)t.EventIndex + t.EventCount));
        Metadata.Fields = Records(global, Base(0x194), fields, 8, GenshinRecords.Field);
        Metadata.Properties = Records(global, Base(0x7C), properties, 10, GenshinRecords.Property);
        Metadata.Events = Records(global, Base(0x180), events, 14, GenshinRecords.Event);
        var parameters = Metadata.Methods.Max(m => m.ParameterCount == 0 ? 0 : checked((int)m.ParameterStart + m.ParameterCount));
        Metadata.Params = Records(global, Base(0x174), parameters, 8, GenshinRecords.Parameter);
        Metadata.NestedTypeIndices = Records(
            global,
            Base(0x1A0),
            types.Max(t => t.NestedTypeCount == 0 ? 0 : (int)t.NestedTypeIndex + t.NestedTypeCount),
            4,
            (d, _) => I32(d, 0)
        );
        Metadata.InterfaceUsageIndices = Records(
            global,
            Base(0xC8),
            types.Max(t => t.InterfacesCount == 0 ? 0 : (int)t.InterfacesIndex + t.InterfacesCount),
            4,
            (d, _) => new TypeIndex(I32(d, 0))
        );
        Metadata.InterfaceOffsets = Records(
            global,
            Base(0x54),
            types.Max(t => t.InterfaceOffsetsCount == 0 ? 0 : (int)t.InterfaceOffsetsStart + t.InterfaceOffsetsCount),
            6,
            (d, _) => new Il2CppInterfaceOffsetPair { InterfaceTypeIndex = I32(d, 0), Offset = I16(d, 4) }
        );
        Metadata.VTableMethodIndices = Records(
            global,
            Base(0x134),
            types.Max(t => t.VTableCount == 0 ? 0 : t.VTableIndex + t.VTableCount),
            4,
            (d, _) => U32(d, 0)
        );
        // Exported type indices are not consumed downstream; read them only to validate the table.
        var exportedTypeIndices = Records(
            global,
            Base(0x90),
            checked((int)Metadata.Images.Sum(i => (long)i.ExportedTypeCount)),
            4,
            (d, _) => I32(d, 0)
        );
        if (exportedTypeIndices.Any(i => (uint)i >= types.Length))
            throw new InvalidDataException("Exported type index out of range.");
        Metadata.Assemblies = Records(startup, Slot(0xCC), Metadata.Images.Length, 16, ReadAssembly);
        var containers = types.Select(t => (int)t.GenericContainerIndex).Concat(Metadata.Methods.Select(m => (int)m.GenericContainerIndex)).Max() + 1;
        Metadata.GenericContainers = Records(global, Base(0x140), containers, 16, GenshinRecords.Container);
        var genericParameters = Metadata.GenericContainers.Max(c => (int)c.GenericParameterStart + c.TypeArgc);
        Metadata.GenericParameters = Records(global, Base(0x94), genericParameters, 14, GenshinRecords.GenericParameter);
        var constraints = Metadata.GenericParameters.Max(p => p.ConstraintsCount == 0 ? 0 : p.ConstraintsStart + p.ConstraintsCount);
        Metadata.GenericConstraintIndices = Records(global, Base(0x1DC), constraints, 4, (d, _) => new TypeIndex(I32(d, 0)));
        Metadata.FieldDefaultValues = Records(
            global,
            Base(0x1D8),
            Slot(0x1F4) / 12,
            12,
            (d, _) =>
                new Il2CppFieldDefaultValue
                {
                    DataIndex = I32(d, 4),
                    FieldIndex = I32(d, 8),
                    TypeIndex = I32(d, 0),
                }
        );
        Metadata.ParameterDefaultValues = Records(
            global,
            Base(0x138),
            Slot(0xE0) / 12,
            12,
            (d, _) =>
                new Il2CppParameterDefaultValue
                {
                    ParameterIndex = I32(d, 4),
                    TypeIndex = I32(d, 0),
                    DataIndex = I32(d, 8),
                }
        );
        var attributes = checked((int)Metadata.Images.Sum(i => (long)i.CustomAttributeCount));
        Metadata.AttributeTypeRanges = Records(
            global,
            Base(0x1C8),
            attributes,
            8,
            (d, _) =>
                new Il2CppCustomAttributeTypeRange
                {
                    Token = U32(d, 0),
                    Start = (int)(U32(d, 4) & 0xFFFFFF),
                    Count = (int)(U32(d, 4) >> 24),
                }
        );
        var attributeTypes = Metadata.AttributeTypeRanges.Max(a => a.Start + a.Count);
        Metadata.AttributeTypeIndices = Records(global, Base(0x160), attributeTypes, 4, (d, _) => I32(d, 0));
        Metadata.FieldRefs = Records(
            global,
            Base(0x19C),
            TableCount(0x19C, 0x178, 8),
            8,
            (d, i) =>
            {
                var x = unchecked(((0xD370UL * (uint)i) ^ 0x5885A579UL) * 0x50151122UL);
                var k = unchecked(((uint)(x >> 13) ^ 0x239D3016u) * 0x5E2E58B6u + 0x7B41E727u);
                return new Il2CppFieldRef
                {
                    TypeIndex = unchecked((int)(U32(d, 0) ^ k ^ 0x014BE62Eu)),
                    FieldIndex = unchecked((int)((U32(d, 4) + 0x8760702Au) ^ k)),
                };
            }
        );
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
            Intern(f.NameIndex);
        foreach (var m in Metadata.Methods)
            Intern(m.NameIndex);
        foreach (var p in Metadata.Params)
            Intern(p.NameIndex);
        foreach (var p in Metadata.Properties)
            Intern(p.NameIndex);
        foreach (var e in Metadata.Events)
            Intern(e.NameIndex);
        foreach (var p in Metadata.GenericParameters)
            Intern(p.NameIndex);
        foreach (var i in Metadata.Images)
            Intern(i.NameIndex);
        foreach (var assembly in Metadata.Assemblies)
        {
            Intern(assembly.Aname.NameIndex);
            Intern(assembly.Aname.CultureIndex);
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
            return "";
        var offset = (int)(index & 0xFFFFFF);
        var seed = unchecked(((0x3EEED96861341169UL * (ulong)offset) ^ 0x30F39D3F643E5C2DUL) * 0xDD6ACDB15078B176UL + 0x34847BCE8197A491UL);
        return DecodeBlocks(global, Base(0x70) + offset, (int)(index >> 24), seed, 0x11E1A912269F936DUL);
    }

    private void ReadLiterals()
    {
        // 0x14052D3B2 indexes offsets at 1E4; the following 94 table bounds
        // 87,252 literals plus one sentinel. Usage+54 selects an eager usage group.
        var count = TableCount(0x1E4, 0x94, 4) - 1;
        var literals = new string[count];
        for (var i = 0; i < count; i++)
        {
            var start = checked((int)LiteralOffset(i));
            var length = checked((int)LiteralOffset(i + 1) - start);
            if (length < 0)
                throw new InvalidDataException("Literal offsets are not monotonic.");
            literals[i] = DecodeBlocks(global, Base(0x124) + start, length, LiteralSeed(i), 0x7233CAD939D03CBCUL);
        }
        Metadata.StringLiterals = literals;
    }

    // Literal getter 0x14052F9F0; offset table consumer 0x140530FFA.
    private uint LiteralOffset(int i)
    {
        var x = unchecked(((0x84FDUL * (uint)i) ^ 0x44A4DE7CUL) * 0x2A04E474UL);
        var key = (uint)(x >> 23) ^ 0xC6A574CCu;
        return unchecked((U32(global, Base(0x1E4) + i * 4) ^ 0x732E7F05u) + key + 0xC5E65791u);
    }

    private static ulong LiteralSeed(int i) =>
        unchecked(((0x59D948494A127F4AUL * (uint)i + 0x9DD295036E0638CAUL) ^ 0x580C38B94622EF76UL) + 0x4165529C1F55FC4EUL);

    // 0x14053FF8E initializes encoded runtime assemblies; cancel its secondary
    // runtime biases to recover stock assembly identities and version numbers.
    private Il2CppAssemblyDefinition ReadAssembly(ReadOnlySpan<byte> d, int i)
    {
        var x = unchecked((0x4B29530D129EUL * (uint)i + 0x8AE9095EF1F4UL) ^ 0x30B17184UL);
        var ka = unchecked((uint)((x * 0x5C23B9CDUL + 0x1149331A49F9EUL) >> 17));
        var n = startup.AsSpan(Slot(0xFC) + i * 44, 44);
        var k = unchecked(0x4E6E2D16u * ((0x0667CD2Cu * (uint)i) ^ 0x23572FA1u));
        var name = new Il2CppAssemblyNameDefinition
        {
            NameIndex = unchecked((int)((U32(n, 8) + 0xABD2582Eu) ^ k)),
            CultureIndex = unchecked((int)((U32(n, 0x1C) + 0xF879A51Au) ^ k)),
            PublicKeyIndex = -1,
            HashValueIndex = -1,
            HashAlg = (AssemblyHashAlgorithm)(U32(n, 0x10) ^ k ^ 0x5412A6A9u),
            HashLen = unchecked((int)(U32(n, 4) ^ k ^ 0x48AEF163u)),
            Flags = unchecked((AssemblyNameFlags)((U32(n, 0x18) + 0xAFB6B407u) ^ k)),
            Major = unchecked((int)((U32(n, 0xC) + 0xBE0A3BCDu) ^ k)),
            Minor = unchecked((int)(U32(n, 0) ^ k ^ 0x51FEE62Du)),
            Build = unchecked((int)(U32(n, 0x14) ^ k ^ 0x3E683E99u)),
            Revision = unchecked((int)(U32(n, 0x20) ^ k ^ 0x276C14B3u)),
        };
        for (var b = 0; b < 8; b++)
            name.PublicKeyToken[b] = n[0x24 + b];
        return new()
        {
            ImageIndex = unchecked((int)((U32(d, 8) + 0xCB26AE04u) ^ ka)),
            Token = U32(d, 0) ^ ka ^ 0x31B72849u,
            ReferencedAssemblyStart = unchecked((int)((U32(d, 4) + 0xDC174E25u) ^ ka)),
            ReferencedAssemblyCount = unchecked((int)(U32(d, 0xC) ^ ka ^ 0x7E5CF70Du)),
            Aname = name,
        };
    }

    private static readonly (int Offset, int Kind, uint Key)[] HeaderCiphers =
    [
        (0x00C, 0, 0x178B3A88u),
        (0x01C, 1, 0x85490F40u),
        (0x024, 0, 0x323BAE70u),
        (0x02C, 0, 0x317F338Fu),
        (0x044, 1, 0xE42E197Fu),
        (0x048, 1, 0xFEF771F0u),
        (0x050, 1, 0xE113BA5Cu),
        (0x054, 1, 0x8054C847u),
        (0x058, 0, 0x2A663E35u),
        (0x05C, 1, 0xA204B3BBu),
        (0x060, 1, 0x848299C0u),
        (0x064, 1, 0xA7932555u),
        (0x070, 0, 0x18202CCCu),
        (0x078, 1, 0xDA43F87Du),
        (0x07C, 1, 0xAD974A19u),
        (0x090, 1, 0xE1281080u),
        (0x094, 1, 0xD97E8A5Cu),
        (0x09C, 0, 0x4B5CDCA3u),
        (0x0A0, 0, 0x4B0AA3Fu),
        (0x0A8, 1, 0xF2473285u),
        (0x0AC, 1, 0xE5C3616Du),
        (0x0B0, 0, 0x51324A1Fu),
        (0x0B8, 1, 0xE94B5F9Au),
        (0x0BC, 0, 0x1E6FC854u),
        (0x0C0, 1, 0xF1D266E1u),
        (0x0C4, 1, 0xBC98D5E5u),
        (0x0C8, 0, 0x1D590619u),
        (0x0CC, 1, 0x808A48F3u),
        (0x0D0, 1, 0xE1492096u),
        (0x0E0, 1, 0xCDA9945Au),
        (0x0EC, 0, 0x3CF0720Cu),
        (0x0F4, 0, 0x43310F1Fu),
        (0x0F8, 1, 0xBB08313Eu),
        (0x0FC, 1, 0x835C9D53u),
        (0x104, 1, 0x938D8470u),
        (0x110, 1, 0xA3D9CC8Bu),
        (0x114, 1, 0xF0EEB388u),
        (0x118, 1, 0xE87220CDu),
        (0x11C, 1, 0xDA286331u),
        (0x120, 1, 0xFAA13BC2u),
        (0x124, 0, 0x36C4F639u),
        (0x130, 1, 0xED493C2Au),
        (0x134, 1, 0xAF707941u),
        (0x138, 0, 0x7139CE6Bu),
        (0x140, 0, 0x649E2FD4u),
        (0x144, 1, 0xBB6784E1u),
        (0x14C, 1, 0xDE642132u),
        (0x154, 1, 0x936A8A40u),
        (0x158, 1, 0xBD71F12Fu),
        (0x160, 1, 0xA45526D7u),
        (0x164, 0, 0x6201B054u),
        (0x168, 1, 0xB01C236Du),
        (0x170, 0, 0x6347382Bu),
        (0x174, 1, 0xAE6BCD68u),
        (0x178, 0, 0x25AF313Eu),
        (0x180, 1, 0xD551CAFCu),
        (0x18C, 0, 0x4F44791Au),
        (0x194, 0, 0x536B48B9u),
        (0x19C, 0, 0x005D3D0Cu),
        (0x1A0, 1, 0x95721A53u),
        (0x1C0, 1, 0xB84FFD78u),
        (0x1C4, 1, 0xD374EC03u),
        (0x1C8, 1, 0xDB2820C6u),
        (0x1CC, 1, 0x9C0BD319u),
        (0x1D8, 1, 0xDE66F77Au),
        (0x1DC, 0, 0x11E455E1u),
        (0x1E4, 1, 0x9DEB89A5u),
        (0x1EC, 0, 0x7256D7C1u),
        (0x1F4, 0, 0x159A5178u),
        (0x204, 0, 0x16BB22A7u),
        (0x208, 1, 0xA18364A3u),
    ];
}
