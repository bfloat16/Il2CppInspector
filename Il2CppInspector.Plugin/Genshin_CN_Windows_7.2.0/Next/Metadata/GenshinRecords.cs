using System.Reflection;
using static Il2CppInspector.MoraxReader;

namespace Il2CppInspector.Next.Metadata;

// YuanShen 7.2.0: type constructor 0x15A4D6B60; member constructors 0x14051C480..0x140532210.
// Image/assembly initialization 0x14053FBFF; generic parameter records 0x1404E2350.
internal static class GenshinRecords
{
    internal static uint MethodKey(int i) => unchecked((uint)(((0x1639UL * (uint)i) ^ 0x5E01C03BUL) * 0x106B250C17F34CCBUL + 0x069FFDCBE218FAF4UL));

    internal static uint FieldKey(int i) =>
        unchecked((uint)((0x380B5207UL * ((0xAE93UL * (uint)i) ^ 0x112D0196UL)) >> 12) * 0x4F038C54u + 0x2C41E5A3u);

    internal static uint PropertyKey(int i) => unchecked((uint)((0x224C5F1F284080C6UL * (uint)i + 0x133FEB8C78BA02AEUL) >> 13));

    internal static uint EventKey(int i) =>
        unchecked((uint)(((((0x3D201A8B1C47UL * (uint)i + 0x073B66AD95628295UL) >> 15) * 0x2BADF621UL >> 17) * 0x670DDC32UL) >> 8)) ^ 0x12883EA9u;

    internal static Il2CppTypeDefinition Type(ReadOnlySpan<byte> d, int i)
    {
        var bf = U16(d, 0x32) ^ 0x7BF1;
        // Bits8/9 select private vtables; the upper nibble supplies declared Pack.
        var packing = (uint)(bf >> 12);
        var canonical = (uint)(bf & 0x3F) | packing << 6 | (uint)(bf & 0xC00) | packing << 12;
        return new()
        {
            NameIndex = unchecked((int)(U32(d, 0x18) + 0xA285A06Du)),
            NamespaceIndex = unchecked((int)(U32(d, 0x14) ^ 0x149E96F1u)),
            ByValTypeIndex = unchecked((int)(U32(d, 0x1C) ^ 0x44AFD57Du)),
            ByRefTypeIndex = unchecked((int)(U32(d, 4) + 0xEF395092u)),
            DeclaringTypeIndex = unchecked((int)(U32(d, 0x10) + 0x8B5290C0u)),
            ParentIndex = unchecked((int)(U32(d, 0) + 0xA0A35767u)),
            ElementTypeIndex = unchecked((int)(U32(d, 0x20) ^ 0x30C910E5u)),
            GenericContainerIndex = Index16((ushort)(U16(d, 0x3E) ^ 0x78F6)),
            Flags = (TypeAttributes)(U32(d, 0x2C) ^ 0x5A65E232u),
            FieldIndex = unchecked((int)(U32(d, 8) ^ 0x72239B10u)),
            MethodIndex = unchecked((int)(U32(d, 0xC) ^ 0x5ABB91BDu)),
            PropertyIndex = unchecked((int)(U32(d, 0x24) + 0xFBC9B097u)),
            EventIndex = Index16(unchecked((ushort)(U16(d, 0x34) + 0x7F77))),
            NestedTypeIndex = Index16((ushort)(U16(d, 0x3C) ^ 0x977C)),
            InterfacesIndex = Index16(unchecked((ushort)(U16(d, 0x36) + 0x6F6C))),
            InterfaceOffsetsStart = Index16(unchecked((ushort)(U16(d, 0x30) + 0xE42A))),
            VTableIndex = unchecked((int)(U32(d, 0x28) + 0x9AF313C0u)),
            FieldCount = unchecked((ushort)(U16(d, 0x38) + 0x3664)),
            MethodCount = unchecked((ushort)(U16(d, 0x3A) + 0xBF1A)),
            PropertyCount = (byte)(d[0x45] ^ 0x69),
            EventCount = unchecked((byte)(d[0x42] + 0x81)),
            NestedTypeCount = (byte)(d[0x40] ^ 0xBE),
            InterfacesCount = (byte)(d[0x43] ^ 0x25),
            InterfaceOffsetsCount = unchecked((byte)(d[0x41] + 0xE3)),
            VTableCount = (byte)(d[0x44] ^ 0xF4),
            Bitfield = Il2CppTypeDefinitionBitfield.FromRawValue(canonical),
            Token = 0x02000000u | (uint)i,
        };
    }

    internal static Il2CppMethodDefinition Method(ReadOnlySpan<byte> d, int i)
    {
        var k = MethodKey(i);
        return new()
        {
            NameIndex = unchecked((int)(U32(d, 8) - k + 0xDE01C441u)),
            ReturnType = unchecked((int)(U32(d, 0) - k + 0xD4E0B12Eu)),
            ParameterStart = unchecked((int)(U32(d, 4) - k + 0xC05565F9u)),
            GenericContainerIndex = Index16(unchecked((ushort)((U16(d, 0xE) ^ 0x8B1A) - k))),
            DeclaringType = unchecked((int)(U32(d, 0x14) + ((0x1639u * (uint)i) ^ 0x5E01C03Bu) * 0xE80CB335u + 0xAF2C1E6Du)),
            Flags = unchecked((ushort)((U16(d, 0x10) ^ 0xDC05) - k)),
            ImplFlags = unchecked((ushort)((U16(d, 0xC) ^ 0x2B8D) - k)),
            Slot = unchecked((ushort)(U16(d, 0x12) - k + 0x58B5)),
            ParameterCount = unchecked((byte)((d[0x18] ^ 0xF9) - k)),
            MethodIndex = i,
            InvokerIndex = -1,
            ReversePInvokeWrapperIndex = -1,
            RgctxStartIndex = -1,
            Token = 0x06000000u | (uint)i,
        };
    }

    internal static Il2CppFieldDefinition Field(ReadOnlySpan<byte> d, int i)
    {
        var k = FieldKey(i);
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 0) ^ 0x2C691D30u) - k)),
            TypeIndex = unchecked((int)((U32(d, 4) ^ 0x7A3E6276u) - k)),
            Token = 0x04000000u | (uint)i,
        };
    }

    internal static Il2CppParameterDefinition Parameter(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked(((uint)((0x74B075FDA13UL * (uint)i + 0x3362D8F50E637AUL) >> 22) ^ 0x599EEE90u) + 0x214E875Du);
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 0) + 0xA587B2CDu) ^ k)),
            TypeIndex = unchecked((int)((U32(d, 4) + 0xDB1D6B24u) ^ k)),
            Token = 0x08000000u | (uint)i,
        };
    }

    internal static Il2CppPropertyDefinition Property(ReadOnlySpan<byte> d, int i)
    {
        var k = PropertyKey(i);
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 0) + 0xB7888C3Fu) ^ k)),
            Get = Index16(unchecked((ushort)((U16(d, 8) + 0x781F) ^ k))),
            Attrs = unchecked((PropertyAttributes)(ushort)((U16(d, 4) - 0x2ADA) ^ k)),
            Set = Index16(unchecked((ushort)((U16(d, 6) + 0x7A2E) ^ k))),
            Token = 0x17000000u | (uint)i,
        };
    }

    internal static Il2CppEventDefinition Event(ReadOnlySpan<byte> d, int i)
    {
        var k = EventKey(i);
        return new()
        {
            NameIndex = unchecked((int)(U32(d, 4) - k + 0xAC2CD069u)),
            TypeIndex = unchecked((int)((U32(d, 0) ^ 0x167CBFDDu) - k)),
            Remove = Index16(unchecked((ushort)(U16(d, 8) - k + 0x6A5F))),
            Add = Index16(unchecked((ushort)(U16(d, 0xC) - k + 0x112D))),
            Raise = Index16(unchecked((ushort)((U16(d, 0xA) ^ 0x3C8E) - k))),
            Token = 0x14000000u | (uint)i,
        };
    }

    internal static Il2CppImageDefinition Image(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked(((uint)((0x31F46A1515C6UL * (uint)i) >> 15) ^ 0x6E6499ACu) + 0x5F644297u);
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 0x24) + 0xCF946789u) ^ k)),
            AssemblyIndex = unchecked((int)((U32(d, 8) + 0x87A851C7u) ^ k)),
            TypeStart = unchecked((int)((U32(d, 0) + 0xB9ADD5CAu) ^ k)),
            TypeCount = U32(d, 0x14) ^ k ^ 0x56166A8Bu,
            ExportedTypeStart = unchecked((int)(U32(d, 0x20) ^ k ^ 0x09527D38u)),
            ExportedTypeCount = unchecked((U32(d, 0x1C) + 0xC600C638u) ^ k),
            CustomAttributeStart = unchecked((int)(U32(d, 0x10) ^ k ^ 0x4774DDFCu)),
            CustomAttributeCount = unchecked((U32(d, 4) + 0xF8096735u) ^ k),
            EntryPointIndex = unchecked((int)(U32(d, 0xC) ^ k ^ 0x45FC0149u)),
            Token = unchecked((U32(d, 0x18) + 0xEFE8D5EBu) ^ k),
        };
    }

    internal static Il2CppGenericContainer Container(ReadOnlySpan<byte> d, int i)
    {
        var x = unchecked(((0x82F1UL * (uint)i) ^ 0x7F7C8DE6UL) * 0x17E6E15BUL + 0x923FD8CC66UL);
        var k = unchecked((uint)(x >> 9) * 0x2B71CE56u) ^ 0x6DFDB254u;
        return new()
        {
            IsMethod = unchecked((int)((U32(d, 4) + 0x8359BD34u) ^ k)),
            GenericParameterStart = unchecked((int)(U32(d, 8) ^ k ^ 0x6B91F14Fu)),
            TypeArgc = unchecked((int)((U32(d, 0) + 0xAD3B4568u) ^ k)),
            OwnerIndex = unchecked((int)((U32(d, 0xC) + 0xA3ED24D8u) ^ k)),
        };
    }

    internal static Il2CppGenericParameter GenericParameter(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked((uint)(((0x2740F42BB72AUL * (uint)i) >> 12) * 0x61DCE680UL + 0x2F2E5C80UL));
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 0) + 0x8306029Eu) ^ k)),
            ConstraintsStart = unchecked((short)(U16(d, 0xA) ^ k ^ 0xEDD8)),
            Flags = unchecked((ushort)((U16(d, 8) - 0x3215) ^ k)),
            ConstraintsCount = unchecked((short)(U16(d, 0xC) ^ k ^ 0x35D0)),
            Num = unchecked((ushort)((U16(d, 6) - 0x41F0) ^ k)),
            OwnerIndex = Index16(unchecked((ushort)((U16(d, 4) - 0x37E) ^ k))),
        };
    }
}
