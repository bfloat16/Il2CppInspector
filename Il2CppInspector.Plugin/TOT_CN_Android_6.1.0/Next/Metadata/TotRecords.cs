// TOT 6.1.0 Android metadata record readers.
//
// The decrypted container uses stock Unity 2018.4 / IL2CPP metadata v24.1 record layouts
// (verified against Cpp/UnityHeaders/24.1-2018.4.34-2018.4.36.h and against the numeric
// tiling of the shipped file). Only the header itself is custom/permuted, and the
// stringLiteral index table stores {dataIndex, length} instead of stock {length, dataIndex}.
using System.Buffers.Binary;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector
{
    internal static class TotRecords
    {
        internal static ushort U16(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(o, 2));

        internal static short I16(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadInt16LittleEndian(d.Slice(o, 2));

        internal static uint U32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(o, 4));

        internal static int I32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadInt32LittleEndian(d.Slice(o, 4));

        // Stock v24.1 0x64-byte Il2CppTypeDefinition.
        internal static Il2CppTypeDefinition ReadTypeDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = I32(d, 0x00),
                NamespaceIndex = I32(d, 0x04),
                ByValTypeIndex = I32(d, 0x08),
                ByRefTypeIndex = I32(d, 0x0C),
                DeclaringTypeIndex = I32(d, 0x10),
                ParentIndex = I32(d, 0x14),
                ElementTypeIndex = I32(d, 0x18),
                RgctxStartIndex = I32(d, 0x1C),
                RgctxCount = I32(d, 0x20),
                GenericContainerIndex = I32(d, 0x24),
                Flags = (System.Reflection.TypeAttributes)U32(d, 0x28),
                FieldIndex = I32(d, 0x2C),
                MethodIndex = I32(d, 0x30),
                EventIndex = I32(d, 0x34),
                PropertyIndex = I32(d, 0x38),
                NestedTypeIndex = I32(d, 0x3C),
                InterfacesIndex = I32(d, 0x40),
                VTableIndex = I32(d, 0x44),
                InterfaceOffsetsStart = I32(d, 0x48),
                MethodCount = U16(d, 0x4C),
                PropertyCount = U16(d, 0x4E),
                FieldCount = U16(d, 0x50),
                EventCount = U16(d, 0x52),
                NestedTypeCount = U16(d, 0x54),
                VTableCount = U16(d, 0x56),
                InterfacesCount = U16(d, 0x58),
                InterfaceOffsetsCount = U16(d, 0x5A),
                Bitfield = Il2CppTypeDefinitionBitfield.FromRawValue(U32(d, 0x5C)),
                Token = U32(d, 0x60),
            };

        // Stock v24.1 0x34-byte Il2CppMethodDefinition.
        internal static Il2CppMethodDefinition ReadMethodDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = I32(d, 0x00),
                DeclaringType = I32(d, 0x04),
                ReturnType = I32(d, 0x08),
                ParameterStart = I32(d, 0x0C),
                GenericContainerIndex = I32(d, 0x10),
                MethodIndex = I32(d, 0x14),
                InvokerIndex = I32(d, 0x18),
                ReversePInvokeWrapperIndex = I32(d, 0x1C),
                RgctxStartIndex = I32(d, 0x20),
                RgctxCount = I32(d, 0x24),
                Token = U32(d, 0x28),
                Flags = U16(d, 0x2C),
                ImplFlags = U16(d, 0x2E),
                Slot = U16(d, 0x30),
                ParameterCount = U16(d, 0x32),
            };

        // Stock v24.1 0x0C-byte {nameIndex, typeIndex, token}.
        internal static Il2CppFieldDefinition ReadFieldDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = I32(d, 0),
                TypeIndex = I32(d, 4),
                Token = U32(d, 8),
            };

        // Stock v24.1 0x0C-byte {nameIndex, token, typeIndex}.
        internal static Il2CppParameterDefinition ReadParameterDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = I32(d, 0),
                Token = U32(d, 4),
                TypeIndex = I32(d, 8),
            };

        // Stock v24.1 0x14-byte {nameIndex, get, set, attrs, token}.
        internal static Il2CppPropertyDefinition ReadPropertyDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = I32(d, 0),
                Get = I32(d, 4),
                Set = I32(d, 8),
                Attrs = (System.Reflection.PropertyAttributes)U32(d, 12),
                Token = U32(d, 16),
            };

        // Stock v24.1 0x18-byte {nameIndex, typeIndex, add, remove, raise, token}.
        internal static Il2CppEventDefinition ReadEventDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = I32(d, 0),
                TypeIndex = I32(d, 4),
                Add = I32(d, 8),
                Remove = I32(d, 12),
                Raise = I32(d, 16),
                Token = U32(d, 20),
            };

        // Stock v24.1 0x28-byte Il2CppImageDefinition.
        internal static Il2CppImageDefinition ReadImageDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = I32(d, 0),
                AssemblyIndex = I32(d, 4),
                TypeStart = I32(d, 8),
                TypeCount = U32(d, 12),
                ExportedTypeStart = I32(d, 16),
                ExportedTypeCount = U32(d, 20),
                EntryPointIndex = I32(d, 24),
                Token = U32(d, 28),
                CustomAttributeStart = I32(d, 32),
                CustomAttributeCount = U32(d, 36),
            };

        // Stock v24.1 0x40-byte Il2CppAssemblyDefinition. The v24.1 assembly-name record is the
        // compact 48-byte form (no hashValueIndex), so nameIndex sits at +0x10.
        internal static Il2CppAssemblyDefinition ReadAssemblyDefinition(ReadOnlySpan<byte> d)
        {
            var name = new Il2CppAssemblyNameDefinition
            {
                NameIndex = I32(d, 16),
                CultureIndex = I32(d, 20),
                HashValueIndex = -1,
                PublicKeyIndex = I32(d, 24),
                HashAlg = (System.Reflection.AssemblyHashAlgorithm)U32(d, 28),
                HashLen = I32(d, 32),
                Flags = (System.Reflection.AssemblyNameFlags)U32(d, 36),
                Major = I32(d, 40),
                Minor = I32(d, 44),
                Build = I32(d, 48),
                Revision = I32(d, 52),
            };
            d.Slice(56, 8).CopyTo(name.PublicKeyToken);
            return new()
            {
                ImageIndex = I32(d, 0),
                Token = U32(d, 4),
                ReferencedAssemblyStart = I32(d, 8),
                ReferencedAssemblyCount = I32(d, 12),
                Aname = name,
            };
        }

        // Stock v24.1 0x10-byte {ownerIndex, typeArgc, isMethod, genericParameterStart}.
        internal static Il2CppGenericContainer ReadGenericContainer(ReadOnlySpan<byte> d) =>
            new()
            {
                OwnerIndex = I32(d, 0),
                TypeArgc = I32(d, 4),
                IsMethod = I32(d, 8),
                GenericParameterStart = I32(d, 12),
            };

        // Stock v24.1 0x10-byte {ownerIndex, nameIndex, constraintsStart(i16), constraintsCount(i16), num, flags}.
        internal static Il2CppGenericParameter ReadGenericParameter(ReadOnlySpan<byte> d) =>
            new()
            {
                OwnerIndex = I32(d, 0),
                NameIndex = I32(d, 4),
                ConstraintsStart = I16(d, 8),
                ConstraintsCount = I16(d, 10),
                Num = U16(d, 12),
                Flags = U16(d, 14),
            };

        // Stock v24.1 0x08-byte {interfaceTypeIndex, offset}.
        internal static Il2CppInterfaceOffsetPair ReadInterfaceOffset(ReadOnlySpan<byte> d) => new() { InterfaceTypeIndex = I32(d, 0), Offset = I32(d, 4) };

        // Stock v24.1 0x08-byte {typeIndex, fieldIndex}.
        internal static Il2CppFieldRef ReadFieldRef(ReadOnlySpan<byte> d) => new() { TypeIndex = I32(d, 0), FieldIndex = I32(d, 4) };

        // Stock v24.1 0x0C-byte {fieldIndex, typeIndex, dataIndex}.
        internal static Il2CppFieldDefaultValue ReadFieldDefaultValue(ReadOnlySpan<byte> d) =>
            new()
            {
                FieldIndex = I32(d, 0),
                TypeIndex = I32(d, 4),
                DataIndex = I32(d, 8),
            };

        // Stock v24.1 0x0C-byte {parameterIndex, typeIndex, dataIndex}.
        internal static Il2CppParameterDefaultValue ReadParameterDefaultValue(ReadOnlySpan<byte> d) =>
            new()
            {
                ParameterIndex = I32(d, 0),
                TypeIndex = I32(d, 4),
                DataIndex = I32(d, 8),
            };

        // Stock v24.1 0x0C-byte {token, start, count} custom-attribute range.
        internal static Il2CppCustomAttributeTypeRange ReadCustomAttributeTypeRange(ReadOnlySpan<byte> d) =>
            new()
            {
                Token = U32(d, 0),
                Start = I32(d, 4),
                Count = I32(d, 8),
            };
    }
}
