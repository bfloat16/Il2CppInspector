// ZZZ 3.3.0 Windows record layouts, decoded from the MORAX loaders and verified against the shipped metadata.
using System.Reflection;
using static Il2CppInspector.MoraxReader;

namespace Il2CppInspector.Next.Metadata;

internal static class Il2CppTypeDefinitionReader
{
    internal static Il2CppTypeDefinition FromZzz(ReadOnlySpan<byte> d) =>
        new()
        {
            ByValTypeIndex = I32(d, 0x20),
            Token = U32(d, 0x10),
            MethodIndex = I32(d, 0x04),
            NameIndex = I32(d, 0x1C),
            ByRefTypeIndex = I32(d, 0x2C),
            Flags = (TypeAttributes)U32(d, 0x00),
            VTableIndex = I32(d, 0x28),
            FieldIndex = I32(d, 0x14),
            NamespaceIndex = I32(d, 0x18),
            ParentIndex = I32(d, 0x24),
            DeclaringTypeIndex = -1,
            ElementTypeIndex = I32(d, 0x0C),
            PropertyIndex = I32(d, 0x30),
            PropertyCount = U16(d, 0x36),
            EventIndex = I16(d, 0x48),
            GenericContainerIndex = Index16(U16(d, 0x44)),
            // Preserve the complete u16, including packing and default-size flags.
            Bitfield = Il2CppTypeDefinitionBitfield.FromRawValue(U16(d, 0x40)),
            NestedTypeCount = U16(d, 0x4E),
            FieldCount = U16(d, 0x42),
            VTableCount = U16(d, 0x46),
            MethodCount = U16(d, 0x4A),
            InterfaceOffsetsCount = U16(d, 0x4C),
            InterfacesCount = U16(d, 0x38),
            NestedTypeIndex = U16(d, 0x3E),
            InterfacesIndex = U16(d, 0x3C),
            EventCount = U16(d, 0x34),
            InterfaceOffsetsStart = U16(d, 0x3A),
        };
}

internal static class Il2CppMethodDefinitionReader
{
    internal static Il2CppMethodDefinition FromZzz(ReadOnlySpan<byte> d) =>
        new()
        {
            Token = U32(d, 0x00),
            DeclaringType = I32(d, 0x04),
            ParameterStart = I32(d, 0x08),
            ReturnType = I32(d, 0x0C),
            NameIndex = I32(d, 0x10),
            ImplFlags = U16(d, 0x14),
            Flags = U16(d, 0x16),
            GenericContainerIndex = Index16(U16(d, 0x18)),
            Slot = U16(d, 0x1A),
            ParameterCount = d[0x1C],
            InvokerIndex = -1,
        };
}

internal static class Il2CppFieldDefinitionReader
{
    internal static Il2CppFieldDefinition FromZzz(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked((uint)((ulong)(0x241ED848u * ((0x3E0D23E91318UL * (uint)i) ^ 0x332E076Au)) >> 0x10));
        return new()
        {
            TypeIndex = unchecked((int)(U32(d, 0) - k - 0xB023B24u)),
            NameIndex = unchecked((int)(U32(d, 4) - k - 0x4D4BFE43u)),
            Token = unchecked((U32(d, 8) ^ 0x266C19D0u) - k),
        };
    }
}

internal static class Il2CppParameterDefinitionReader
{
    internal static Il2CppParameterDefinition FromZzz(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked(0x609E8A6Cu * (uint)(((0xF8FD0DEBBA1UL * (uint)i) >> 0x16) ^ 0x26AD211Cu));
        return new()
        {
            Token = U32(d, 0) ^ k ^ 0x4E0A9DB9u,
            NameIndex = unchecked((int)(U32(d, 4) ^ k ^ 0x7F1A71C5u)),
            TypeIndex = unchecked((int)(U32(d, 8) ^ k ^ 0x3750837Au)),
        };
    }
}

internal static class Il2CppPropertyDefinitionReader
{
    internal static Il2CppPropertyDefinition FromZzz(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked((uint)(((0x799E9C1AUL * ((0x2C3C179D6700UL * (uint)i) >> 0x15) + 0x3BB0256AFB42E216UL) >> 0x17)));
        return new()
        {
            Token = unchecked((U32(d, 0) - k - 0x442428ADu)),
            NameIndex = unchecked((int)((U32(d, 4) ^ 0x77C1D322u) - k)),
            Get = Index16(unchecked((ushort)((U16(d, 8) ^ 0x8CC0) - (ushort)k))),
            Attrs = unchecked((PropertyAttributes)(ushort)((U16(d, 0x0A) ^ 0x2E30) - (ushort)k)),
            Set = Index16(unchecked((ushort)(U16(d, 0x0C) - (ushort)k - 0x122Fu))),
        };
    }
}

internal static class Il2CppEventDefinitionReader
{
    internal static Il2CppEventDefinition FromZzz(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked((uint)((((0x5BE746C7UL * (((0x2B7AUL * (uint)i) + 0x4B81F891U) ^ 0x61049BE3U)) >> 0x14) ^ 0x76903214) + 0x187D646B));
        return new()
        {
            TypeIndex = unchecked((int)((U32(d, 0) - 0x2E0989AAu) ^ k)),
            NameIndex = unchecked((int)(U32(d, 4) ^ 0x437D2B78u ^ k)),
            Token = unchecked((U32(d, 8) ^ 0x1EC8B377u ^ k)),
            Raise = Index16(unchecked((ushort)((U16(d, 0x0C) ^ 0x94F0) ^ k))),
            Add = Index16(unchecked((ushort)((U16(d, 0x0E) - 0x7314) ^ k))),
            Remove = Index16(unchecked((ushort)((U16(d, 0x10) + 0x6649) ^ k))),
        };
    }
}

internal static class Il2CppImageDefinitionReader
{
    internal static Il2CppImageDefinition FromZzz(ReadOnlySpan<byte> d, int i)
    {
        // The loader keeps this key in 64 bits; the record values are truncated to 32 when combined.
        var k = unchecked((uint)((((0x3A0F9DD54E25UL * (uint)i) >> 0x14) + 0x3B33D046)));
        var v139 = k ^ 0x402D24C3u;
        return new()
        {
            CustomAttributeCount = unchecked(U32(d, 0x00) ^ k ^ 0x5F547F0B),
            TypeCount = unchecked((U32(d, 0x04) - 0x34124D87u) ^ v139),
            AssemblyIndex = unchecked((int)((U32(d, 0x08) - 0x3EF17613u) ^ v139)),
            ExportedTypeCount = unchecked((U32(d, 0x0C) - 0x39AC7E01u) ^ k ^ 0x115706F8u ^ 0x517A223B),
            Token = unchecked(U32(d, 0x10) ^ k ^ 0x487AE27D),
            NameIndex = unchecked((int)(U32(d, 0x14) ^ k ^ 0x094475E2u)),
            EntryPointIndex = unchecked((int)((U32(d, 0x18) - 0x62BD574Eu) ^ k ^ 0x0D540611u ^ 0x4D7922D2u)),
            ExportedTypeStart = unchecked((int)(U32(d, 0x1C) ^ k ^ 0x3901098Cu)),
            CustomAttributeStart = unchecked((int)(U32(d, 0x20) ^ k ^ 0x7DD99EA9u ^ 0x31A9C215u)),
            TypeStart = unchecked((int)((U32(d, 0x24) - 0x478EB40Du) ^ k ^ 0x6B50242Du ^ 0x2B7D00EEu)),
        };
    }
}

internal static class Il2CppGenericContainerReader
{
    internal static Il2CppGenericContainer FromZzz(ReadOnlySpan<byte> d, int i)
    {
        var pre = unchecked((uint)((0x326F94C9u * (((uint)i * 0x268Bu) ^ 0x17DE0D96u)) ^ 0x43651645u));
        var k = unchecked(pre + 0x39AE049Bu);
        return new()
        {
            GenericParameterStart = unchecked((int)((U32(d, 0) ^ 0x45E7E894u) - k)),
            OwnerIndex = unchecked((int)(U32(d, 4) - k - 0x6D7482D3u)),
            // The encoded predicate marks type ownership; the stock container stores the inverse.
            IsMethod = unchecked(U32(d, 8) ^ 0x307A5778u) == k ? 0 : 1,
            TypeArgc = unchecked((int)(U32(d, 0x0C) - k - 0x1F0D8E07u)),
        };
    }
}

internal static class Il2CppGenericParameterReader
{
    internal static Il2CppGenericParameter FromZzz(ReadOnlySpan<byte> d) =>
        new()
        {
            NameIndex = I32(d, 0),
            Flags = U16(d, 4),
            ConstraintsStart = I16(d, 6),
            Num = U16(d, 8),
            ConstraintsCount = I16(d, 10),
            OwnerIndex = U16(d, 12),
        };
}

internal static class Il2CppCustomAttributeTypeRangeReader
{
    internal static Il2CppCustomAttributeTypeRange FromZzz(ReadOnlySpan<byte> d) =>
        new()
        {
            Start = (int)(U32(d, 0) & 0xFFFFFF),
            Count = (int)(U32(d, 0) >> 24),
            Token = U32(d, 4),
        };
}

internal static class Il2CppFieldDefaultValueReader
{
    internal static Il2CppFieldDefaultValue FromZzz(ReadOnlySpan<byte> d) =>
        new()
        {
            DataIndex = I32(d, 0),
            TypeIndex = I32(d, 4),
            FieldIndex = I32(d, 8),
        };
}

internal static class Il2CppParameterDefaultValueReader
{
    internal static Il2CppParameterDefaultValue FromZzz(ReadOnlySpan<byte> d) =>
        new()
        {
            TypeIndex = I32(d, 0),
            ParameterIndex = I32(d, 4),
            DataIndex = I32(d, 8),
        };
}

internal static class Il2CppFieldRefReader
{
    internal static Il2CppFieldRef FromZzz(ReadOnlySpan<byte> d, int i)
    {
        var k = unchecked((uint)(((0x3554EE6Bu * (((uint)i * 0x6B68u) ^ 0x34E019E5u)) ^ 0x3EAE6DDDu) + 0x193ACBCF));
        return new() { FieldIndex = unchecked((int)((U32(d, 0) ^ 0x03443EF7u) - k)), TypeIndex = unchecked((int)((U32(d, 4) ^ 0x2105CFF1u) - k)) };
    }
}

internal static class Il2CppInterfaceOffsetPairReader
{
    internal static Il2CppInterfaceOffsetPair FromZzz(ReadOnlySpan<byte> d, int _) => new() { InterfaceTypeIndex = I32(d, 0), Offset = I16(d, 4) };
}
