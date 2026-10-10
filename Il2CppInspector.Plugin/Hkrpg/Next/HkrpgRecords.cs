using System.Reflection;
using Il2CppInspector.Next.Metadata;
using static Il2CppInspector.HkrpgMorax;

namespace Il2CppInspector;

internal static class HkrpgRecords
{
    internal static Il2CppImageDefinition Image(ReadOnlySpan<byte> d, int index)
    {
        var k = unchecked((((uint)index * 0xE07Cu ^ 0x7538159Eu) * 0x120D0703u ^ 0x6032C9D3u) + 0x2EBB0085u);
        return new()
        {
            NameIndex = unchecked((int)(U32(d, 0x0C) ^ k ^ 0x4D648371u)),
            TypeStart = unchecked((int)(U32(d, 0x14) ^ k ^ 0x7BABEEA0u ^ 0x235AEAF5u)),
            TypeCount = U32(d, 4) ^ k ^ 0x10FEA394u ^ 0x7C06D18Cu,
            CustomAttributeStart = unchecked((int)(U32(d, 0x20) ^ k ^ 0x3E69547Au ^ 0x43633AF4u)),
            CustomAttributeCount = unchecked(U32(d, 8) + 0xED46492Fu) ^ k,
            AssemblyIndex = index,
            EntryPointIndex = -1,
            ExportedTypeStart = -1,
            Token = 0x00000001,
        };
    }

    internal static Il2CppTypeDefinition Type(ReadOnlySpan<byte> d, int index)
    {
        unchecked
        {
            return new()
            {
                NameIndex = (int)(U32(d, 0x28) - 0x16029708u),
                NamespaceIndex = (int)(U32(d, 0x24) - 0x0E2CD277u),
                ParentIndex = U32(d, 4) == 0x48F95546u ? -1 : (int)(U32(d, 4) - 0x48F95547u),
                Flags = (TypeAttributes)(U32(d, 0x14) - 0x01127390u),
                FieldIndex = (int)(U32(d, 0x20) - 0x74853864u),
                FieldCount = (ushort)(U16(d, 0x32) + 0x444D),
                MethodIndex = (int)(U32(d, 8) ^ 0x1A7AF5FEu),
                MethodCount = (ushort)(U16(d, 0x34) + 0x5F93),
                PropertyIndex = (int)(U32(d, 0x1C) - 0x39D706EFu),
                PropertyCount = (byte)(d[0x41] + 0x1F),
                EventIndex = U16(d, 0x30) ^ 0x6ECD,
                EventCount = (byte)(d[0x40] ^ 0x73),
                GenericContainerIndex = Index16((ushort)(U16(d, 0x3C) + 0x5404)),
                NestedTypeIndex = U16(d, 0x3A) ^ 0xB2C0,
                NestedTypeCount = (byte)(d[0x43] + 4),
                InterfacesIndex = U16(d, 0x36) ^ 0xC28C,
                InterfacesCount = (byte)(d[0x44] ^ 0xC7),
                Bitfield = Il2CppTypeDefinitionBitfield.FromRawValue(U32(d, 0x38) & 3),
                ByValTypeIndex = -1,
                ByRefTypeIndex = -1,
                DeclaringTypeIndex = -1,
                ElementTypeIndex = -1,
                VTableIndex = -1,
                Token = 0x02000000u | (uint)index,
            };
        }
    }

    internal static Il2CppMethodDefinition Method(ReadOnlySpan<byte> d, int index)
    {
        unchecked
        {
            var v = ((ulong)index * 0x31E1UL ^ 0x33914937UL) * 0x2C03F17DUL >> 23;
            var k = (uint)(v * 0x540CC9F4UL >> 21) + 0x71BC7861u;
            return new()
            {
                NameIndex = (int)(U32(d, 0) ^ k ^ 0x0E714BC1u),
                ParameterStart = (int)(U32(d, 4) ^ k ^ 0x009889B8u),
                ReturnType = (int)((U32(d, 8) - 0x653E0B1Du) ^ k),
                Flags = (ushort)(U16(d, 0x0E) ^ (ushort)k ^ 0x3733),
                ParameterCount = (byte)(d[0x18] ^ (byte)k ^ 0xA8),
                GenericContainerIndex = -1,
                Slot = ushort.MaxValue,
                DeclaringType = -1,
                MethodIndex = index,
                InvokerIndex = -1,
                Token = 0x06000000u | (uint)index,
            };
        }
    }

    internal static Il2CppFieldDefinition Field(ReadOnlySpan<byte> d, int index, int start, int local)
    {
        var k = unchecked(0xAD416BB9u - ((uint)start + 0x74853864u) * 0x2C5DCB00u + (uint)local * 0xD3A23500u);
        return new()
        {
            NameIndex = unchecked((int)(U32(d, 0) + k + 0x2AAFC785u)),
            TypeIndex = unchecked((int)(U32(d, 4) + k)),
            Token = 0x04000000u | (uint)index,
        };
    }

    internal static Il2CppParameterDefinition Parameter(ReadOnlySpan<byte> d, int index)
    {
        var k = unchecked((uint)(((ulong)index * 0x72E1D74B12BUL + 0x1911D05AFF5UL) >> 11) * 0x58B870A2u - 0x7C3084BCu);
        return new()
        {
            TypeIndex = unchecked((int)((U32(d, 0) ^ 0x67E90DC5u) - k)),
            NameIndex = unchecked((int)((U32(d, 4) ^ 0x7103092Eu) - k)),
            Token = 0x08000000u | (uint)index,
        };
    }

    internal static Il2CppPropertyDefinition Property(ReadOnlySpan<byte> d, int index)
    {
        var v = unchecked(((ulong)index * 6035UL ^ 0x280E7A20UL) * 0x6D28A1EFUL) >> 17;
        var k = unchecked(((uint)v ^ 0x727EFF5Bu) + 0x633C43D6u) ^ 0x4ADBD505u;
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 0) ^ 0x6199063Cu) - k)),
            Get = Index16(unchecked((ushort)((U16(d, 6) ^ 0xFA36) - (ushort)k))),
            Set = Index16(unchecked((ushort)((U16(d, 4) ^ 0x4B8F) - (ushort)k))),
            Token = 0x17000000u | (uint)index,
        };
    }

    internal static Il2CppEventDefinition Event(ReadOnlySpan<byte> d, int index)
    {
        var v = unchecked(((ulong)index * 0x3CDCUL ^ 0x550D63BAUL) * 0x3F07C46CUL) >> 19;
        var k = unchecked((uint)((v * 0x5F4660F8UL + 0x26824684CA69CA48UL) >> 15));
        return new()
        {
            NameIndex = unchecked((int)((U32(d, 0) ^ 0x078981CBu) - k)),
            TypeIndex = unchecked((int)((U32(d, 4) ^ 0x53243DF3u) - k)),
            Raise = Index16(unchecked((ushort)((U16(d, 8) ^ 0x8450) - (ushort)k))),
            Add = Index16(unchecked((ushort)((U16(d, 10) ^ 0xE8CB) - (ushort)k))),
            Remove = Index16(unchecked((ushort)((U16(d, 12) ^ 0x3CE2) - (ushort)k))),
            Token = 0x14000000u | (uint)index,
        };
    }

    internal static Il2CppGenericContainer Container(ReadOnlySpan<byte> d, int index)
    {
        var v = unchecked((ulong)index * 0x3D6913E0AF40UL + 0x0A64CAD60FA052C0UL);
        v = unchecked((v >> 23) * 0x770E3FE8UL);
        var k = unchecked((uint)(((v >> 11) * 0x2C9A0EA3UL) >> 23));
        var ownerKind = unchecked((U32(d, 4) - k) ^ 0x75CD676Fu);
        if (ownerKind is not (0 or 1 or 31))
            throw new InvalidDataException($"Invalid HSR generic container owner kind {ownerKind} at {index}.");
        return new()
        {
            OwnerIndex = unchecked((int)(U32(d, 8) ^ k ^ 0x687A16BAu)),
            IsMethod = ownerKind == 0 ? 0 : 1,
            TypeArgc = unchecked((int)((U32(d, 0) - 0x0CCEBB89u) ^ k)),
            GenericParameterStart = unchecked((int)((U32(d, 12) + 0xF12AF1B1u) ^ k)),
        };
    }

    internal static Il2CppGenericParameter GenericParameter(ReadOnlySpan<byte> d, int index)
    {
        var v = unchecked((ulong)index * 0x617FE3CC452CUL + 0x09DC5DB71F0EB440UL);
        v = unchecked((v >> 9) + 0x2AD8C631UL) ^ 0x5278374DUL;
        var k = unchecked((uint)(v * 0x4AADBD4BUL >> 15));
        return new()
        {
            NameIndex = unchecked((int)(U32(d, 0) - k - 0x44888123u)),
            OwnerIndex = unchecked((ushort)((U16(d, 8) ^ 0x7526) - k)),
            Num = unchecked((ushort)((U16(d, 10) ^ 0xBD3B) - k)),
        };
    }
}
