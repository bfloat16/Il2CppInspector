using System.Reflection;
using static Il2CppInspector.GenshinMorax;

namespace Il2CppInspector.Next.Metadata;

// Physical records are shuffled and each field retains its own arithmetic order.
// Index-derived keys and narrowing use the exact unsigned machine width.
internal static class GenshinRecords
{
    internal static uint MethodKey(int i) => unchecked(0x39036375u * ((0x7572748Au * ((0x4E94u * (uint)i) ^ 0x62D8B7B8u) + 0x1F4D9EEAu) ^ 0x6E8931D6u));

    internal static uint FieldKey(int i) => unchecked((uint)((0x7F76A166UL * ((0x23CFUL * (uint)i) ^ 0x3D27C42EUL) + 0xF06651FC0B8AUL) >> 16) + 0x49ACDCBCu);

    internal static uint PropertyKey(int i) => unchecked((0x5CD0B62Eu * ((0xA593u * (uint)i) ^ 0x3CA3FFA0u) + 0x63A341E6u) ^ 0x2411ED71u);

    internal static uint EventKey(int i) => unchecked((uint)((0x1BF89D8B64959BB7UL * ((0x9C54UL * (uint)i) ^ 0x1F0DA79FUL) + 0x295EE0EBA6CAFAEAUL) >> 15));

    internal static Il2CppTypeDefinition Type(ReadOnlySpan<byte> d, int i)
    {
        var bf = U16(d, 0x32) ^ 0x8BA4;
        // The upper nibble supplies declared Pack. Bits8/9 describe private vtables.
        // Preserve the raw flags separately in the adapter for runtime layout decisions.
        var packing = (uint)(bf >> 12);
        var canonical = (uint)(bf & 0x3F) | packing << 6 | (uint)(bf & 0xC00) | packing << 12;
        return new()
        {
            NameIndex = unchecked((int)(U32(d, 0x14) - 0x17EE0887u)),
            NamespaceIndex = unchecked((int)(U32(d, 0x18) ^ 0x666769B5u)),
            ByValTypeIndex = unchecked((int)(U32(d, 0x10) ^ 0x37EEAE45u)),
            ByRefTypeIndex = unchecked((int)(U32(d, 0x28) ^ 0x1B7494B5u)),
            DeclaringTypeIndex = unchecked((int)(U32(d, 0x1C) ^ 0x2145AFB6u)),
            ParentIndex = unchecked((int)(U32(d, 0x2C) ^ 0x5FCD8771u)),
            ElementTypeIndex = unchecked((int)(U32(d, 0x04) - 0x76217192u)),
            GenericContainerIndex = Index16(unchecked((ushort)(U16(d, 0x3E) - 0x1398))),
            Flags = unchecked((TypeAttributes)(U32(d, 0x0C) - 0x205F6070u)),
            FieldIndex = unchecked((int)(U32(d, 0) ^ 0x72628007u)),
            MethodIndex = unchecked((int)(U32(d, 8) ^ 0x0478AF59u)),
            PropertyIndex = unchecked((int)(U32(d, 0x24) - 0x31DEBE34u)),
            EventIndex = Index16(unchecked((ushort)(U16(d, 0x38) - 0x2955))),
            NestedTypeIndex = Index16((ushort)(U16(d, 0x36) ^ 0xC87C)),
            InterfacesIndex = Index16(unchecked((ushort)(U16(d, 0x34) + 0x68ED))),
            InterfaceOffsetsStart = Index16((ushort)(U16(d, 0x3A) ^ 0x4BCE)),
            VTableIndex = unchecked((int)(U32(d, 0x20) ^ 0x6C9A5100u)),
            FieldCount = (ushort)(U16(d, 0x30) ^ 0x9A6B),
            MethodCount = (ushort)(U16(d, 0x3C) ^ 0x8811),
            PropertyCount = (byte)(d[0x43] ^ 0x86),
            EventCount = (byte)(d[0x44] ^ 0xB5),
            NestedTypeCount = (byte)(d[0x45] ^ 0xAB),
            InterfacesCount = unchecked((byte)(d[0x41] - 0x39)),
            InterfaceOffsetsCount = unchecked((byte)(d[0x42] - 0x5E)),
            VTableCount = (byte)(d[0x40] ^ 6),
            Bitfield = Il2CppTypeDefinitionBitfield.FromRawValue(canonical),
            Token = 0x02000000u | (uint)i,
        };
    }

    internal static Il2CppMethodDefinition Method(ReadOnlySpan<byte> d, int i)
    {
        var k = MethodKey(i);
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 4) ^ 0x0613ACE3u) - k)),
            ReturnType = unchecked((int)(U32(d, 0) - k - 0x1A11BF7Fu)),
            ParameterStart = unchecked((int)((U32(d, 8) ^ 0x20D55D01u) - k)),
            GenericContainerIndex = Index16(unchecked((ushort)((U16(d, 0xC) ^ 0x1E2A) - k))),
            DeclaringType = unchecked((int)((U32(d, 0xE) ^ 0x0394729Cu) - k)),
            Flags = unchecked((ushort)(U16(d, 0x12) - k - 0x7269)),
            ImplFlags = unchecked((ushort)(U16(d, 0x14) - k + 0xAA81)),
            Slot = unchecked((ushort)(U16(d, 0x16) - k + 0x4D9F)),
            ParameterCount = unchecked((byte)(d[0x18] - k - 0x74)),
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
            NameIndex = unchecked((int)(U32(d, 0) ^ k ^ 0x048E6373u)),
            TypeIndex = unchecked((int)((U32(d, 4) + 0xB36B852Du) ^ k ^ 0x4F6E2A1Du)),
            Token = 0x04000000u | (uint)i,
        };
    }

    internal static Il2CppParameterDefinition Parameter(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked(((uint)((0x2B0352A0UL * ((0x4A23UL * (uint)i) ^ 0x5F268FCFUL)) >> 9) ^ 0x1028EA75u) + 0x68B376F1u) ^ 0x4F86A723u;
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 4) ^ 0x6B95F108u) - k)),
            TypeIndex = unchecked((int)((U32(d, 0) ^ 0x76621606u) - k)),
            Token = 0x08000000u | (uint)i,
        };
    }

    internal static Il2CppPropertyDefinition Property(ReadOnlySpan<byte> d, int i)
    {
        var k = PropertyKey(i);
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 0) - 0x11776A16u) ^ k)),
            Get = Index16(unchecked((ushort)((U16(d, 4) - 0x238F) ^ k))),
            Attrs = unchecked((PropertyAttributes)(ushort)((U16(d, 6) + 0x720A) ^ k)),
            Set = Index16(unchecked((ushort)(U16(d, 8) ^ k ^ 0xDB1))),
            Token = 0x17000000u | (uint)i,
        };
    }

    internal static Il2CppEventDefinition Event(ReadOnlySpan<byte> d, int i)
    {
        var k = EventKey(i);
        return new()
        {
            NameIndex = unchecked((int)(U32(d, 0) ^ k ^ 0x05C273EDu)),
            TypeIndex = unchecked((int)((U32(d, 4) - 0x68638257u) ^ k)),
            Remove = Index16(unchecked((ushort)((U16(d, 8) + 0x38EB) ^ k))),
            Raise = Index16(unchecked((ushort)((U16(d, 0xA) - 0x1BF6) ^ k))),
            Add = Index16(unchecked((ushort)(U16(d, 0xC) ^ k ^ 0x7D45))),
            Token = 0x14000000u | (uint)i,
        };
    }

    internal static Il2CppImageDefinition Image(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked(((uint)((0xBD1E70EB664UL * (uint)i) >> 16) + 0x724F4CCBu) ^ 0x2BE02F3Du);
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 0x18) ^ 0x5E8998A9u) - k)),
            AssemblyIndex = unchecked((int)((U32(d, 0xC) ^ 0x1066FCA3u) - k)),
            TypeStart = unchecked((int)(U32(d, 0x10) - k - 0x5F5774F6u)),
            TypeCount = unchecked((U32(d, 0x24) ^ 0x40B24EE6u) - k),
            ExportedTypeStart = unchecked((int)(U32(d, 4) - k - 0x44B4AEF0u)),
            ExportedTypeCount = unchecked(U32(d, 8) - k - 0x699ED4D4u),
            CustomAttributeStart = unchecked((int)((U32(d, 0x14) ^ 0x0F90FC6Du) - k)),
            CustomAttributeCount = unchecked((U32(d, 0) ^ 0x57631D22u) - k),
            EntryPointIndex = unchecked((int)(U32(d, 0x1C) - k - 0x52C9017Cu)),
            Token = unchecked(U32(d, 0x20) - k - 0x29E22224u),
        };
    }

    internal static Il2CppGenericContainer Container(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked((((((0x8187u * (uint)i) ^ 0x42D5A190u) * 0x7B2D0E18u + 0xB6F00C08u) ^ 0x7A3E254Du) + 0x71555646u));
        return new()
        {
            IsMethod = unchecked((int)((U32(d, 0) - 0x71215917u) ^ k)),
            GenericParameterStart = unchecked((int)(U32(d, 4) ^ k ^ 0x2BAD4D8Cu)),
            TypeArgc = unchecked((int)(U32(d, 8) ^ k ^ 0x07F8A45Cu)),
            OwnerIndex = unchecked((int)(U32(d, 0xC) ^ k ^ 0x5C56350Bu)),
        };
    }

    internal static Il2CppGenericParameter GenericParameter(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked((uint)((0x6F1C0337A0897415UL * (uint)i + 0x31061C4212D60D89UL) >> 22));
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 0) - 0x61AD664Fu) ^ k)),
            ConstraintsStart = unchecked((short)((U16(d, 4) + 0x5AAF) ^ k)),
            Flags = unchecked((ushort)((U16(d, 6) - 0x4084) ^ k)),
            ConstraintsCount = unchecked((short)(U16(d, 8) ^ k ^ 0xEF3A)),
            Num = unchecked((ushort)(U16(d, 0xA) ^ k ^ 0xFBF5)),
            OwnerIndex = Index16(unchecked((ushort)((U16(d, 0xC) - 0x571E) ^ k))),
        };
    }
}
