// 恋与深空 6.0.0 Android metadata records (Unity 2019.4.34f1 fork "X3Engine").
// Plain 0x58-byte type definitions are built at 0x2471EF0; keyed 0x20-byte methods at 0x2473C48.
// Field defaults are plaintext (0x2473870), parameter defaults are XOR 0x1C13 (0x2473988).
using System.Buffers.Binary;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector.Plugin.LAD
{
    internal static class LadRecords
    {
        internal static ushort U16(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(o, 2));

        internal static short I16(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadInt16LittleEndian(d.Slice(o, 2));

        internal static uint U32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(o, 4));

        internal static int I32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadInt32LittleEndian(d.Slice(o, 4));

        // Methods, events, properties and parameter defaults store keyed -1 sentinels.
        internal static int X32(ReadOnlySpan<byte> d, int o, uint key) => unchecked((int)(U32(d, o) ^ key));

        // 0x2471EF0 reads plain byval/declaring/parent/element indexes at +0x08/+0x0C/+0x10/+0x14.
        // There is no byrefTypeIndex; Common resolves by-ref types from Il2CppType.ByRef.
        internal static Il2CppTypeDefinition ReadTypeDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = I32(d, 0x00),
                NamespaceIndex = I32(d, 0x04),
                ByValTypeIndex = I32(d, 0x08),
                ByRefTypeIndex = I32(d, 0x08),
                DeclaringTypeIndex = I32(d, 0x0C),
                ParentIndex = I32(d, 0x10),
                ElementTypeIndex = I32(d, 0x14),
                GenericContainerIndex = I32(d, 0x18),
                Flags = (System.Reflection.TypeAttributes)U32(d, 0x1C),
                FieldIndex = I32(d, 0x20),
                MethodIndex = I32(d, 0x24),
                EventIndex = I32(d, 0x28),
                PropertyIndex = I32(d, 0x2C),
                NestedTypeIndex = I32(d, 0x30),
                InterfacesIndex = I32(d, 0x34),
                VTableIndex = I32(d, 0x38),
                InterfaceOffsetsStart = I32(d, 0x3C),
                MethodCount = U16(d, 0x40),
                PropertyCount = U16(d, 0x42),
                FieldCount = U16(d, 0x44),
                EventCount = U16(d, 0x46),
                NestedTypeCount = U16(d, 0x48),
                VTableCount = U16(d, 0x4A),
                InterfacesCount = U16(d, 0x4C),
                InterfaceOffsetsCount = U16(d, 0x4E),
                Bitfield = Il2CppTypeDefinitionBitfield.FromRawValue(U32(d, 0x50)),
                Token = U32(d, 0x54),
            };

        // 0x2473C48 XORs the first six dwords, flags (+0x18) and slot (+0x1C) with 0x686.
        // Implementation flags and parameter count remain plaintext.
        internal static Il2CppMethodDefinition ReadMethodDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = X32(d, 0x00, LadBuild.MethodKey),
                DeclaringType = X32(d, 0x04, LadBuild.MethodKey),
                ReturnType = X32(d, 0x08, LadBuild.MethodKey),
                ParameterStart = X32(d, 0x0C, LadBuild.MethodKey),
                GenericContainerIndex = X32(d, 0x10, LadBuild.MethodKey),
                Token = U32(d, 0x14) ^ LadBuild.MethodKey,
                Flags = (ushort)(U16(d, 0x18) ^ (ushort)LadBuild.MethodKey),
                ImplFlags = U16(d, 0x1A),
                Slot = (ushort)(U16(d, 0x1C) ^ (ushort)LadBuild.MethodKey),
                ParameterCount = U16(d, 0x1E),
            };

        // Stock 0x0C-byte {nameIndex, typeIndex, token}.
        internal static Il2CppFieldDefinition ReadFieldDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = I32(d, 0),
                TypeIndex = I32(d, 4),
                Token = U32(d, 8),
            };

        // Stock 0x0C-byte {nameIndex, token, typeIndex}.
        internal static Il2CppParameterDefinition ReadParameterDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = I32(d, 0),
                Token = U32(d, 4),
                TypeIndex = I32(d, 8),
            };

        // Stock 0x14-byte {nameIndex, get, set, attrs, token}, every dword XOR 0x2479.
        internal static Il2CppPropertyDefinition ReadPropertyDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = X32(d, 0, LadBuild.PropertyKey),
                Get = X32(d, 4, LadBuild.PropertyKey),
                Set = X32(d, 8, LadBuild.PropertyKey),
                Attrs = (System.Reflection.PropertyAttributes)(U32(d, 12) ^ LadBuild.PropertyKey),
                Token = U32(d, 16) ^ LadBuild.PropertyKey,
            };

        // Stock 0x18-byte {nameIndex, typeIndex, add, remove, raise, token}, every dword XOR 0xF52.
        internal static Il2CppEventDefinition ReadEventDefinition(ReadOnlySpan<byte> d) =>
            new()
            {
                NameIndex = X32(d, 0, LadBuild.EventKey),
                TypeIndex = X32(d, 4, LadBuild.EventKey),
                Add = X32(d, 8, LadBuild.EventKey),
                Remove = X32(d, 12, LadBuild.EventKey),
                Raise = X32(d, 16, LadBuild.EventKey),
                Token = U32(d, 20) ^ LadBuild.EventKey,
            };

        // Stock v24.5 0x28-byte Il2CppImageDefinition.
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

        // Stock v24.5/v29 0x40-byte assembly; 0x2472C9C copies its inline name.
        internal static Il2CppAssemblyDefinition ReadAssemblyDefinition(ReadOnlySpan<byte> d)
        {
            var name = new Il2CppAssemblyNameDefinition
            {
                NameIndex = I32(d, 0x10),
                CultureIndex = I32(d, 0x14),
                HashValueIndex = -1,
                PublicKeyIndex = I32(d, 0x18),
                HashAlg = (System.Reflection.AssemblyHashAlgorithm)U32(d, 0x1C),
                HashLen = I32(d, 0x20),
                Flags = (System.Reflection.AssemblyNameFlags)U32(d, 0x24),
                Major = I32(d, 0x28),
                Minor = I32(d, 0x2C),
                Build = I32(d, 0x30),
                Revision = I32(d, 0x34),
            };
            d.Slice(0x38, 8).CopyTo(name.PublicKeyToken);
            return new()
            {
                ImageIndex = I32(d, 0),
                Token = U32(d, 4),
                ReferencedAssemblyStart = I32(d, 8),
                ReferencedAssemblyCount = I32(d, 12),
                Aname = name,
            };
        }

        // Stock 0x10-byte {ownerIndex, typeArgc, isMethod, genericParameterStart}.
        internal static Il2CppGenericContainer ReadGenericContainer(ReadOnlySpan<byte> d) =>
            new()
            {
                OwnerIndex = I32(d, 0),
                TypeArgc = I32(d, 4),
                IsMethod = I32(d, 8),
                GenericParameterStart = I32(d, 12),
            };

        // Stock 0x10-byte {ownerIndex, nameIndex, constraintsStart(i16), constraintsCount(i16), num, flags}.
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

        // Field defaults are plaintext triples (0x2473870); parameter triples are keyed (0x2473988).
        internal static Il2CppFieldDefaultValue ReadFieldDefaultValue(ReadOnlySpan<byte> d) =>
            new()
            {
                FieldIndex = I32(d, 0),
                TypeIndex = I32(d, 4),
                DataIndex = I32(d, 8),
            };

        internal static Il2CppParameterDefaultValue ReadParameterDefaultValue(ReadOnlySpan<byte> d) =>
            new()
            {
                ParameterIndex = X32(d, 0, LadBuild.ParameterDefaultValueKey),
                TypeIndex = X32(d, 4, LadBuild.ParameterDefaultValueKey),
                DataIndex = X32(d, 8, LadBuild.ParameterDefaultValueKey),
            };
    }
}
