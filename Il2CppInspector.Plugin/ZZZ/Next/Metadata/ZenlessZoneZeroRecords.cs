// ZZZ 3.2.0 Windows record layouts, ported from ZZZ-MORAX's IDA-verified readers.
// The plugin initializes stock records through internal setters; public properties stay read-only.
using System.Reflection;
using static Il2CppInspector.ZzzMorax;

namespace Il2CppInspector.Next.Metadata
{
    internal static class Il2CppTypeDefinitionReader
    {
        internal static Il2CppTypeDefinition FromZzz(ReadOnlySpan<byte> d) =>
            new()
            {
                ByValTypeIndex = I32(d, 0),
                Token = U32(d, 4),
                MethodIndex = I32(d, 8),
                NameIndex = I32(d, 0x0C),
                ByRefTypeIndex = I32(d, 0x10),
                Flags = (TypeAttributes)U32(d, 0x14),
                VTableIndex = I32(d, 0x18),
                FieldIndex = I32(d, 0x1C),
                NamespaceIndex = I32(d, 0x20),
                ParentIndex = I32(d, 0x24),
                DeclaringTypeIndex = -1,
                ElementTypeIndex = I32(d, 0x2C),
                PropertyIndex = I32(d, 0x30),
                PropertyCount = U16(d, 0x34),
                EventIndex = I16(d, 0x36),
                GenericContainerIndex = Index16(U16(d, 0x38)),
                Bitfield = Il2CppTypeDefinitionBitfieldReader.FromZzz(U16(d, 0x3A)),
                NestedTypeCount = U16(d, 0x3C),
                FieldCount = U16(d, 0x3E),
                VTableCount = U16(d, 0x40),
                MethodCount = U16(d, 0x42),
                InterfaceOffsetsCount = U16(d, 0x44),
                InterfacesCount = U16(d, 0x46),
                NestedTypeIndex = U16(d, 0x48),
                InterfacesIndex = U16(d, 0x4C),
                EventCount = U16(d, 0x4E),
                InterfaceOffsetsStart = U16(d, 0x4A),
            };
    }

    internal static class Il2CppTypeDefinitionBitfieldReader
    {
        // Preserve the complete u16, including packing and default-size flags.
        internal static Il2CppTypeDefinitionBitfield FromZzz(ushort value) => Il2CppTypeDefinitionBitfield.FromRawValue(value);
    }

    internal static class Il2CppMethodDefinitionReader
    {
        internal static Il2CppMethodDefinition FromZzz(ReadOnlySpan<byte> d) =>
            new()
            {
                Token = U32(d, 0),
                ParameterStart = I32(d, 4),
                DeclaringType = I32(d, 8),
                ReturnType = I32(d, 0x0C),
                NameIndex = I32(d, 0x10),
                GenericContainerIndex = Index16(U16(d, 0x14)),
                Slot = U16(d, 0x16),
                ImplFlags = U16(d, 0x18),
                Flags = U16(d, 0x1A),
                ParameterCount = d[0x1C],
                InvokerIndex = -1,
            };
    }

    internal static class Il2CppFieldDefinitionReader
    {
        internal static Il2CppFieldDefinition FromZzz(ReadOnlySpan<byte> d, int i)
        {
            var k = unchecked((uint)(0x113B174C78E2UL * (ulong)i >> 18) ^ 0x52D3857Fu) + 0x6FFE2614u;
            return new()
            {
                NameIndex = unchecked((int)((U32(d, 0) + 0xE16F8E54u) ^ k)),
                Token = unchecked(U32(d, 4) + 0xD6B656CAu) ^ k,
                TypeIndex = unchecked((int)((U32(d, 8) + 0xF04B070Eu) ^ k)),
            };
        }
    }

    internal static class Il2CppParameterDefinitionReader
    {
        internal static Il2CppParameterDefinition FromZzz(ReadOnlySpan<byte> d, int i)
        {
            var v = unchecked(0x288AAE5CD12BUL * (ulong)i + 0x1B5770128E59UL);
            var k = unchecked((((uint)(v >> 14) + 0x6E8C49ACu) ^ 0x6B516F1Du) + 0x3C4B202Fu);
            return new()
            {
                NameIndex = unchecked((int)(k ^ (U32(d, 0) - 0x36F90B44u))),
                Token = k ^ U32(d, 4) ^ 0x6885EDADu,
                TypeIndex = unchecked((int)(k ^ U32(d, 8) ^ 0x0E25BEFCu)),
            };
        }
    }

    internal static class Il2CppPropertyDefinitionReader
    {
        internal static Il2CppPropertyDefinition FromZzz(ReadOnlySpan<byte> d, int i)
        {
            // Every multiply before truncation is 64-bit (including i >= 95,857).
            var k = unchecked((uint)(0x3BE6A110UL * (0x1D43740BUL * (0xAF06UL * (ulong)i ^ 0x17B76A56UL) >> 16) + 0x0894E311D182C800UL));
            return new()
            {
                NameIndex = unchecked((int)((U32(d, 0) ^ 0x67C1EB79u) - k)),
                Token = unchecked((U32(d, 4) ^ 0x1E5522A4u) - k),
                Get = Index16(unchecked((ushort)((U16(d, 10) ^ 0xC14B) - (ushort)k))),
                Set = Index16(unchecked((ushort)(U16(d, 12) - (ushort)k + 0x344F))),
                Attrs = 0,
            };
        }
    }

    internal static class Il2CppEventDefinitionReader
    {
        internal static Il2CppEventDefinition FromZzz(ReadOnlySpan<byte> d, int i)
        {
            var v = unchecked((0x3031UL * (ulong)i ^ 0x140A25ABUL) * 0x22FB12F9UL) >> 12;
            var k = unchecked(((uint)v + 0x7D8001F2u ^ 0x24E7A299u) + 0x665B62FAu);
            return new()
            {
                TypeIndex = unchecked((int)(U32(d, 0) - k - 0x1E175602u)),
                NameIndex = unchecked((int)(U32(d, 4) - k - 0x7DCF662Cu)),
                Token = unchecked((U32(d, 8) ^ 0x1C4782A9u) - k),
                Raise = Index16(unchecked((ushort)(U16(d, 12) - (ushort)k - 0x5669))),
                Add = Index16(unchecked((ushort)(U16(d, 14) - (ushort)k + 0x13A7))),
                Remove = Index16(unchecked((ushort)((U16(d, 16) ^ 0x47D9) - (ushort)k))),
            };
        }
    }

    internal static class Il2CppImageDefinitionReader
    {
        internal static Il2CppImageDefinition FromZzz(ReadOnlySpan<byte> d, int i)
        {
            var k = unchecked((uint)((((ulong)i * 0x4766 + 0x50C91EB5) ^ 0x70F0A290) * 0x15464B76F78EUL + 0x03FE93E2B6D77EUL >> 18));
            return new()
            {
                CustomAttributeCount = unchecked(U32(d, 0) + 0xEB1FC0CAu) ^ k,
                EntryPointIndex = unchecked((int)(U32(d, 4) ^ k ^ 0x6804199Au ^ 0x5BFA7165u)),
                NameIndex = unchecked((int)((U32(d, 8) + 0xC8F81ED8u) ^ k)),
                CustomAttributeStart = unchecked((int)(U32(d, 12) ^ k ^ 0x2FFAC6B7u ^ 0x75131B55u)),
                ExportedTypeCount = unchecked(U32(d, 16) + 0x899E74EAu) ^ k,
                TypeCount = unchecked(U32(d, 20) + 0xECEDAAFEu) ^ k,
                ExportedTypeStart = unchecked((int)(U32(d, 24) ^ k ^ 0x46D6A134u)),
                TypeStart = unchecked((int)((U32(d, 28) + 0xBF484FAAu) ^ k)),
                Token = unchecked(U32(d, 32) + 0xCD9043C3u) ^ k,
                AssemblyIndex = unchecked((int)(U32(d, 36) ^ k ^ 0x612DAE84u)),
            };
        }
    }

    internal static class Il2CppGenericContainerReader
    {
        internal static Il2CppGenericContainer FromZzz(ReadOnlySpan<byte> d, int i)
        {
            var k = unchecked(0x5115FEC3u * (0x45A07F6Fu * ((0x3D2Cu * ((uint)i & 0xFFFF) + 0x6F48E77Au) ^ 0x1FAF6023u) ^ 0x565C8119u));
            return new()
            {
                OwnerIndex = unchecked((int)(U32(d, 0) ^ k ^ 0x1A1BCDB7u)),
                TypeArgc = unchecked((int)((U32(d, 4) + 0xC302C17Bu) ^ k)),
                GenericParameterStart = unchecked((int)((U32(d, 8) + 0xAE99FA67u) ^ k)),
                IsMethod = unchecked((int)(U32(d, 12) - k - 0x46138759u)),
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
                TypeIndex = I32(d, 0),
                FieldIndex = I32(d, 4),
                DataIndex = I32(d, 8),
            };
    }

    internal static class Il2CppParameterDefaultValueReader
    {
        internal static Il2CppParameterDefaultValue FromZzz(ReadOnlySpan<byte> d) =>
            new()
            {
                DataIndex = I32(d, 0),
                TypeIndex = I32(d, 4),
                ParameterIndex = I32(d, 8),
            };
    }

    internal static class Il2CppFieldRefReader
    {
        internal static Il2CppFieldRef FromZzz(ReadOnlySpan<byte> d, int i)
        {
            var k = unchecked(((((uint)i * 0x605Au ^ 0x575ECFFEu) * 0x11BFAB49u + 0x5D50D6CCu) ^ 0x43E07AA6u) + 0x2A719D27u);
            return new() { TypeIndex = unchecked((int)((U32(d, 0) + 0xFF2B5F3Au) ^ k)), FieldIndex = unchecked((int)((U32(d, 4) + 0xDC4B8194u) ^ k)) };
        }
    }

    internal static class Il2CppInterfaceOffsetPairReader
    {
        internal static Il2CppInterfaceOffsetPair FromZzz(ReadOnlySpan<byte> d, int _) => new() { InterfaceTypeIndex = I32(d, 0), Offset = I16(d, 4) };
    }
}
