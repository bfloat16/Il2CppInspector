// BH3 9.1.0 Windows record layouts, ported from the IDA-verified MORAX readers in
// _morax_analysis/C_record_layouts.md and _morax_analysis/E_generic_and_literal.md.
// The plugin initializes stock records through internal setters; public properties stay read-only.
using System.Reflection;
using static Il2CppInspector.Bh3Morax;

namespace Il2CppInspector.Next.Metadata
{
    internal static class Il2CppTypeDefinitionReader
    {
        // C §4 / G §2 / B.2: the type definition table uses per-field constant keys (no index
        // keystream). +0x00 is the byref Il2CppType index and +0x34 the byval one (B.2 reads
        // klass+0x98 this_arg from +0x00 and klass+0x88 byval_arg from +0x34).
        internal static Il2CppTypeDefinition FromBh3(ReadOnlySpan<byte> d) =>
            new()
            {
                ByRefTypeIndex = unchecked((int)(U32(d, 0x00) - 0x44E63C89u)),
                InterfaceOffsetsStart = unchecked((int)(U32(d, 0x04) - 0x607099ACu)),
                PropertyIndex = unchecked((int)(U32(d, 0x08) - 0x2EF7232Cu)),
                NameIndex = unchecked((int)(U32(d, 0x0C) - 0xDDBB2F8u)),
                // G §2.1: +0x10 is the parent Il2CppType index, not a byref type.
                ParentIndex = unchecked((int)(U32(d, 0x10) - 0x7159A7E6u)),
                VTableIndex = unchecked((int)(U32(d, 0x14) - 0x3B0A868Du)),
                Flags = (TypeAttributes)unchecked(U32(d, 0x18) ^ 0x41E26BAFu),
                NamespaceIndex = unchecked((int)(U32(d, 0x20) ^ 0x28F643CEu)),
                FieldIndex = unchecked((int)(U32(d, 0x24) ^ 0x2B5AE409u)),
                DeclaringTypeIndex = unchecked((int)(U32(d, 0x28) - 0x642EE579u)),
                Token = unchecked(U32(d, 0x2C) - 0x655746C0u),
                MethodIndex = unchecked((int)(U32(d, 0x30) ^ 0x58E32EE7u)),
                ByValTypeIndex = unchecked((int)(U32(d, 0x34) + 0xBC20238Fu)),
                InterfaceOffsetsCount = unchecked((ushort)(U16(d, 0x38) + 0x24FDu)),
                PropertyCount = unchecked((ushort)(U16(d, 0x3A) - 0x233Bu)),
                NestedTypeIndex = unchecked((ushort)(U16(d, 0x3C) + 0x6680u)),
                EventIndex = unchecked((ushort)(U16(d, 0x3E) ^ 0xE109u)),
                // G §2.3 / §2.4: the four fields C §4 left unnamed.
                NestedTypeCount = unchecked((ushort)(U16(d, 0x40) + 0x4934u)),
                FieldCount = unchecked((ushort)(U16(d, 0x42) - 0x4E06u)),
                // G §2.2: 0xCB11 decodes to -1 (no generic container).
                GenericContainerIndex = Index16(unchecked((ushort)(U16(d, 0x44) + 0x34EEu))),
                InterfacesCount = unchecked((ushort)(U16(d, 0x46) - 0x10F3u)),
                EventCount = unchecked((ushort)(U16(d, 0x48) ^ 0xFECAu)),
                Bitfield = Il2CppTypeDefinitionBitfieldReader.FromBh3(d[0x4A]),
                InterfacesIndex = unchecked((ushort)(U16(d, 0x4C) - 0x4C33u)),
                MethodCount = unchecked((ushort)(U16(d, 0x4E) - 0x3277u)),
                VTableCount = unchecked((ushort)(U16(d, 0x50) + 0x2DA5u)),
                // sub_18061EF00 uses +0x1C for enum element types. This also covers the five
                // stripped enums whose field names (including value__) are empty.
                ElementTypeIndex = unchecked((int)(U32(d, 0x1C) ^ 0x077E65B7u)),
            };
    }

    internal static class Il2CppTypeDefinitionBitfieldReader
    {
        // sub_18061EF00 maps the packed byte into separate runtime flags. In particular the
        // enum bit is inverted; treating this byte as the stock bitfield makes System.String an enum.
        internal static Il2CppTypeDefinitionBitfield FromBh3(byte value) =>
            Il2CppTypeDefinitionBitfield.FromRawValue((uint)((value & 1) | (~value & 2) | ((value + 2) & 0x0C) | ((value + 0x12) & 0x10) | ((value - 0x0E) & 0x20)));
    }

    internal static class Il2CppMethodDefinitionReader
    {
        // C §3 / B.2: stride 30, every field is (raw OP imm) ^ ks32(index).
        internal static Il2CppMethodDefinition FromBh3(ReadOnlySpan<byte> d, int i)
        {
            var k = Ks(i);
            return new()
            {
                ReturnType = unchecked((int)((U32(d, 0x00) - 0x19C0E240u) ^ k)),
                DeclaringType = unchecked((int)(U32(d, 0x04) ^ k ^ 0x651439A0u)),
                NameIndex = unchecked((int)((U32(d, 0x08) - 0x4AF60CD5u) ^ k)),
                ParameterStart = unchecked((int)((U32(d, 0x0C) - 0x135DFC14u) ^ k)),
                Token = unchecked((U32(d, 0x10) - 0x0C3DC77Fu) ^ k),
                GenericContainerIndex = Index16(unchecked((ushort)((U16(d, 0x14) + 0x595Bu) ^ k))),
                // +0x16 contains MethodAttributes (including RTSpecialName=0x1000),
                // +0x18 contains MethodImplAttributes, and +0x1A is the virtual slot (-1 allowed).
                Flags = unchecked((ushort)((U16(d, 0x16) - 0x6255u) ^ k)),
                ImplFlags = unchecked((ushort)(U16(d, 0x18) ^ k ^ 0x49B3u)),
                Slot = unchecked((ushort)((U16(d, 0x1A) + 0x24E0u) ^ k)),
                ParameterCount = unchecked((byte)(d[0x1C] ^ k ^ 0xBEu)),
                InvokerIndex = -1,
            };
        }

        // C §1: the whole keystream is computed in 64 bits; truncating the first multiply to 32 bits
        // breaks every index at or above 76398 (= ceil(2^32 / 0xDB9B)).
        private static uint Ks(int i)
        {
            var x = unchecked((ulong)(uint)i * 0xDB9BUL);
            x ^= 0x1A58D018UL;
            x = unchecked(x * 0x1339AE8BUL);
            x ^= 0x1FFFA93EUL;
            x = unchecked(x * 0x27F47E6AUL);
            x += 0x0BF9490F71BB2A5AUL;
            return unchecked((uint)(x >> 0x14));
        }
    }

    internal static class Il2CppFieldDefinitionReader
    {
        // C A.1: stride 12, index keystream. Verified against all 362,445 token prefixes (0x04).
        internal static Il2CppFieldDefinition FromBh3(ReadOnlySpan<byte> d, int i)
        {
            var k = Ks(i);
            return new()
            {
                NameIndex = unchecked((int)(U32(d, 0x00) ^ k ^ 0x1DE059D1u)),
                Token = unchecked((U32(d, 0x04) - 0x60731DB4u) ^ k ^ 0x6C0D9DD8u),
                TypeIndex = unchecked((int)((U32(d, 0x08) - 0x6624E955u) ^ k ^ 0x6C0D9DD8u)),
            };
        }

        private static uint Ks(int i)
        {
            var x = unchecked((ulong)(uint)i * 0x6947UL + 0x3D6595ABUL);
            x ^= 0x211C466EUL;
            x = unchecked(x * 0x1D02A562UL) >> 0x11;
            x = unchecked(x * 0x3BB4F156UL) >> 0x17;
            return unchecked((uint)x);
        }
    }

    internal static class Il2CppParameterDefinitionReader
    {
        // C A.4: stride 12, index keystream. Verified against all 460,434 token prefixes (0x08).
        internal static Il2CppParameterDefinition FromBh3(ReadOnlySpan<byte> d, int i)
        {
            var k = unchecked(0x617E2CE1u * (uint)i) ^ 0xD122F2E2u;
            return new()
            {
                NameIndex = unchecked((int)(U32(d, 0x00) + k + 0x8216A49Au)),
                Token = unchecked(U32(d, 0x04) + k + 0x524010D4u),
                TypeIndex = unchecked((int)(U32(d, 0x08) + k + 0x9402E9D8u)),
            };
        }
    }

    internal static class Il2CppPropertyDefinitionReader
    {
        // C A.2: stride 14, index keystream. Verified against all 70,114 token prefixes (0x17).
        internal static Il2CppPropertyDefinition FromBh3(ReadOnlySpan<byte> d, int i)
        {
            var k = Ks(i);
            return new()
            {
                Token = unchecked((U32(d, 0x00) - 0x3D585E2Du) ^ k),
                NameIndex = unchecked((int)((U32(d, 0x04) + 0xB0826154u) ^ k)),
                Get = Index16(unchecked((ushort)(U16(d, 0x08) ^ k ^ 0x276Eu))),
                // +0x0A decodes to a constant zero in every record (C A.2).
                Attrs = 0,
                Set = Index16(unchecked((ushort)((U16(d, 0x0C) - 0x2A70u) ^ k))),
            };
        }

        private static uint Ks(int i)
        {
            var x = unchecked((ulong)(uint)i * 0x75FFUL) ^ 0x7BA84751UL;
            x = unchecked(x * 0x7C777911UL);
            x += 0x3A8A589EF2B616B4UL;
            x ^= 0x5532A478UL;
            return unchecked((uint)(x + 0x7C535968UL));
        }
    }

    internal static class Il2CppEventDefinitionReader
    {
        // C A.3 / D A.3: stride 18, index keystream.
        internal static Il2CppEventDefinition FromBh3(ReadOnlySpan<byte> d, int i)
        {
            var k = Ks(i);
            return new()
            {
                // C A.3: the stored value is the pure token plus the EventInfo poison tag 0x1B9C4DC9
                // (D A.3 quotes only the first subtraction, which yields token + 0x1B9C4DC9).
                Token = unchecked(U32(d, 0x00) - k - 0x4D437194u - 0x1B9C4DC9u),
                TypeIndex = unchecked((int)(U32(d, 0x04) - k - 0x10EC630Fu)),
                NameIndex = unchecked((int)(U32(d, 0x08) - k - 0x6BF0002Du)),
                // C A.3 lists the sentinel raw values 0x7DE8 / 0xFFFF / 0xF6C0 for add / raise /
                // remove; each is -1 after the matching adjustment. C and D disagree on the sign of
                // the remove adjustment; C's sentinel arithmetic (0xF6C0 + 0x93F == 0xFFFF) decides it.
                Add = Index16(unchecked((ushort)(U16(d, 0x0C) - k - 0x7DE9u))),
                Raise = Index16(unchecked((ushort)((U16(d, 0x0E) ^ 0x3E23u) - k))),
                Remove = Index16(unchecked((ushort)(U16(d, 0x10) - k + 0x93Fu))),
            };
        }

        private static uint Ks(int i)
        {
            var x = 0xA17BUL * (ulong)(uint)i + 0x6326F11CUL;
            x ^= 0x6C128819UL;
            x = unchecked(x * 0x461DB6E8UL) >> 8;
            x = unchecked(x * 0x39404581UL) >> 0xF;
            return unchecked((uint)(x * 0x6C0E7658UL));
        }
    }

    internal static class Il2CppImageDefinitionReader
    {
        // G §1 / L §1: stride 40, index keystream. Image type and attribute ranges are contiguous.
        internal static Il2CppImageDefinition FromBh3(ReadOnlySpan<byte> d, int i)
        {
            var k = Ks(i);
            return new()
            {
                AssemblyIndex = unchecked((int)((U32(d, 0x00) - 0x254FA9A9u) ^ k)),
                // G §1.4 / §3: typeStart at +0x08, typeCount at +0x10. The two combined keys collapse
                // to one constant (0x5BF4110 ^ 0x68668E68 == 0x6DD9CF78).
                TypeStart = unchecked((int)(U32(d, 0x08) ^ k ^ 0x3A7D48C1u)),
                TypeCount = unchecked((uint)(U32(d, 0x10) ^ k ^ 0x6DD9CF78u)),
                NameIndex = unchecked((int)((U32(d, 0x1C) - 0x34D0442Fu) ^ k)),
                // G §1.3: +0x18 is the entry point index and is -1 for all 212 records.
                EntryPointIndex = -1,
                CustomAttributeStart = unchecked((int)((U32(d, 0x04) - 0x294B7341u) ^ k)),
                CustomAttributeCount = U32(d, 0x14) ^ k ^ 0x4FE44F34u,
                // Exported types are not consumed by the standard output pipeline.
                ExportedTypeStart = 0,
                ExportedTypeCount = 0,
                Token = 0,
            };
        }

        // G §1.2: the corrected image keystream (all 64-bit, truncated to 32 bits at the end).
        // C appendix D.1's constants do not reproduce ksI(0) == 0xAE497FC1.
        private static uint Ks(int i)
        {
            var x = unchecked(0xD331UL * (ulong)(uint)i + 0x209A85AEUL);
            x ^= 0x2F576729UL;
            x = unchecked(x * 0x764D9534UL);
            x >>= 0x13;
            x ^= 0x141FE11BUL;
            x = unchecked(x * 0x53FE4223UL);
            x >>= 0x15;
            return unchecked((uint)x);
        }
    }

    internal static class Il2CppGenericContainerReader
    {
        // E §10: stride 16, index keystream. Verified 564/564 against the parameter owner/prefix sums.
        // Both u16 fields must be masked before the XOR: the raw low half plus the constant can carry
        // past 0xFFFF (containers beyond the first 564 do), which would otherwise inflate the derived
        // parameter count.
        internal static Il2CppGenericContainer FromBh3(ReadOnlySpan<byte> d, int i)
        {
            var k = Ks(i);
            var isMethod = unchecked((int)((U32(d, 0x08) + 0xDC5F9FE7u) ^ k) & 1);
            return new()
            {
                // E §10 gives raw ^ (H(i) ^ is_method) ^ 0x38D3F9A1, but the is_method term is wrong:
                // it differs from the correct value by exactly 1 for every method container, which is
                // why E saw only 563 distinct owner indices out of 564. Dropping it makes all 1,364
                // method containers resolve to the method definition that references them (1364/1364)
                // and leaves the 776 type containers unchanged (is_method is 0 there).
                OwnerIndex = unchecked((int)(U32(d, 0x00) ^ k ^ 0x38D3F9A1u)),
                TypeArgc = unchecked((int)((((U32(d, 0x04) & 0xFFFF) + 0xD2F9u) & 0xFFFF) ^ (k & 0xFFFF))),
                IsMethod = isMethod,
                GenericParameterStart = unchecked((int)((((U32(d, 0x0C) & 0xFFFF) + 0x47F7u) & 0xFFFF) ^ (k & 0xFFFF))),
            };
        }

        private static uint Ks(int i)
        {
            var x = unchecked((ulong)(uint)i * 0x41DAUL);
            x += 0x1FB30CB5UL;
            x ^= 0x174C027DUL;
            x = unchecked(x * 0x7630337BUL) >> 0x12;
            x = unchecked(x * 0x35BDD23BUL) >> 0x0B;
            x = unchecked(x * 0x79E348AFUL) >> 0x0B;
            return unchecked((uint)x);
        }
    }

    internal static class Il2CppGenericParameterReader
    {
        // E §9 / C D.2: stride 14, index keystream. Verified against all 963 names (T, T1, T2, TInput, ...).
        internal static Il2CppGenericParameter FromBh3(ReadOnlySpan<byte> d, int i)
        {
            var k = Ks(i);
            return new()
            {
                NameIndex = unchecked((int)((U32(d, 0x00) + 0xB75155D8u) ^ k)),
                Flags = unchecked((ushort)(U16(d, 0x04) ^ k ^ 0x0EF3u)),
                Num = unchecked((ushort)((U16(d, 0x06) + 0x47B4u) ^ k)),
                OwnerIndex = unchecked((ushort)((U16(d, 0x0A) - 0x7925u) ^ k)),
                // G §5: constraintsStart is the u16 at +0x08 (reading it as u32 would swallow
                // ownerIndex); the constraint indices themselves are the hdr[0x11C] table.
                ConstraintsStart = unchecked((short)(((U16(d, 0x08) - 0x4DE0u) & 0xFFFF) ^ (k & 0xFFFF))),
                ConstraintsCount = unchecked((short)(U16(d, 0x0C) ^ k ^ 0xC476u)),
            };
        }

        private static uint Ks(int i)
        {
            var x = unchecked(0x3B37497FADFUL * (ulong)(uint)i + 0x19F9B91FB4374UL) >> 0x11;
            var k = unchecked((uint)x * 0x174E9474u + 0x1623BE69u);
            return k ^ 0x47B9F7B4u;
        }
    }

    internal static class Il2CppFieldDefaultValueReader
    {
        // E §3: stride 12, plaintext {typeIndex, fieldIndex, dataIndex}.
        internal static Il2CppFieldDefaultValue FromBh3(ReadOnlySpan<byte> d) =>
            new()
            {
                TypeIndex = I32(d, 0x00),
                FieldIndex = I32(d, 0x04),
                DataIndex = I32(d, 0x08),
            };
    }

    internal static class Il2CppParameterDefaultValueReader
    {
        // F G.1: stride 12, plaintext {typeIndex, dataIndex, parameterIndex}; dataIndex 0xFFFFFFFF is null.
        internal static Il2CppParameterDefaultValue FromBh3(ReadOnlySpan<byte> d) =>
            new()
            {
                TypeIndex = I32(d, 0x00),
                DataIndex = I32(d, 0x04),
                ParameterIndex = I32(d, 0x08),
            };
    }

    internal static class Il2CppInterfaceOffsetPairReader
    {
        // F G.2: stride 6, plaintext {u32 typeIndex, u16 offset}.
        internal static Il2CppInterfaceOffsetPair FromBh3(ReadOnlySpan<byte> d) => new() { InterfaceTypeIndex = I32(d, 0x00), Offset = I16(d, 0x04) };
    }
}
