using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInspector.Next;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.LAD
{
    /// <summary>
    /// Metadata adapter for 恋与深空 6.0.0 Android (X3Engine fork of Unity 2019.4.34f1 / metadata v24).
    ///
    /// The 0x100-byte header is permuted: tables are reordered, section offsets are XORed with per-slot keys
    /// and sizes/counts use arithmetic wrappers (loader at 0x2472398). Table extents come from the sorted
    /// decoded offsets; records, including serialized custom attributes, are normalized to v29.
    /// </summary>
    internal sealed class LadMetadata : Plugins.GameMetadataAdapter
    {
        internal readonly Metadata Metadata;
        private readonly byte[] data;
        private readonly int stringDataOffset;
        private readonly int defaultValueDataOffset;
        private readonly List<MetadataUsage> usages = [];
        private Il2CppBinary binary;

        internal LadMetadata(byte[] metadata, EventHandler<string> status, Plugins.GamePlugin plugin)
            : base(plugin)
        {
            data = metadata;
            Metadata = Metadata.CreateForPlugin(metadata, this, MetadataVersions.V290, status);
            status?.Invoke(this, "Decoding LAD_CN_Android_6.0.0 metadata in C#");

            // 0x24732D4 and 0x2489CB8 use the stock v29 attribute and default-value blobs.
            ValidateHeader();
            stringDataOffset = SectionOffset(LadBuild.SlotStrings);
            defaultValueDataOffset = SectionOffset(LadBuild.SlotDefaultValueData);
            ReadMetadata();
        }

        private uint Raw(int slot) => LadRecords.U32(data, slot);

        private int SectionOffset(int slot)
        {
            if (slot == LadBuild.SlotExportedTypes)
                return checked((int)Raw(slot) + LadBuild.ExportedTypesOffsetAddend);
            var key = slot switch
            {
                LadBuild.SlotDefaultValueData => LadBuild.KeyDefaultValueData,
                LadBuild.SlotStrings => LadBuild.KeyStrings,
                LadBuild.SlotFieldMarshaledSizes => LadBuild.KeyFieldMarshaledSizes,
                LadBuild.SlotUnresolvedCallTypes => LadBuild.KeyUnresolvedCallTypes,
                LadBuild.SlotFieldRefs => LadBuild.KeyFieldRefs,
                LadBuild.SlotGenericConstraints => LadBuild.KeyGenericConstraints,
                LadBuild.SlotAttributeRanges => LadBuild.KeyAttributeRanges,
                LadBuild.SlotTypeDefinitions => LadBuild.KeyTypeDefinitions,
                LadBuild.SlotFields => LadBuild.KeyFields,
                LadBuild.SlotInterfaces => LadBuild.KeyInterfaces,
                LadBuild.SlotEvents => LadBuild.KeyEvents,
                LadBuild.SlotProperties => LadBuild.KeyProperties,
                LadBuild.SlotImages => LadBuild.KeyImages,
                LadBuild.SlotMethods => LadBuild.KeyMethods,
                LadBuild.SlotAttributeData => LadBuild.KeyAttributeData,
                LadBuild.SlotParameterDefaultValues => LadBuild.KeyParameterDefaultValues,
                LadBuild.SlotFieldDefaultValues => LadBuild.KeyFieldDefaultValues,
                LadBuild.SlotVTableMethods => LadBuild.KeyVTableMethods,
                LadBuild.SlotParameters => LadBuild.KeyParameters,
                LadBuild.SlotGenericParameters => LadBuild.KeyGenericParameters,
                LadBuild.SlotAssemblies => LadBuild.KeyAssemblies,
                LadBuild.SlotInterfaceOffsets => LadBuild.KeyInterfaceOffsets,
                LadBuild.SlotGenericContainers => LadBuild.KeyGenericContainers,
                LadBuild.SlotNestedTypes => LadBuild.KeyNestedTypes,
                LadBuild.SlotStringLiteralIndex => LadBuild.KeyStringLiteralIndex,
                LadBuild.SlotUnresolvedCallRanges => LadBuild.KeyUnresolvedCallRanges,
                LadBuild.SlotStringLiteralData => LadBuild.KeyStringLiteralData,
                LadBuild.SlotWindowsRuntimeStrings => LadBuild.KeyWindowsRuntimeStrings,
                LadBuild.SlotWindowsRuntimeTypes => LadBuild.KeyWindowsRuntimeTypes,
                _ => throw new InvalidDataException($"LAD metadata slot 0x{slot:X} has no key."),
            };
            return unchecked((int)(Raw(slot) ^ key));
        }

        private void ValidateHeader()
        {
            if (data.Length != LadBuild.MetadataSize)
                throw new InvalidDataException($"LAD metadata size mismatch: 0x{data.Length:X}.");
            if (Raw(0) != LadBuild.SanityEncoded)
                throw new InvalidDataException("LAD metadata sanity check failed.");

            if (Raw(4) != LadBuild.VersionEncoded)
                throw new InvalidDataException("LAD metadata version check failed.");

            // Image/assembly counts go through arithmetic wrappers in the loader (0x2472398/0x2472408).
            var imageCount = (int)((Raw(LadBuild.SlotImages + 4) ^ LadBuild.ImageCountKey) / 0x28);
            var assemblyCount = (int)((Raw(LadBuild.SlotAssemblies + 4) >> (int)LadBuild.AssemblyCountShift) ^ LadBuild.AssemblyCountKey);
            if (imageCount != 154 || assemblyCount != 154)
                throw new NotSupportedException("This X3Engine build is not the supported 恋与深空 6.0.0 layout; keys vary per build.");

            foreach (var slot in Slots)
            {
                var offset = SectionOffset(slot);
                if (offset < 0x100 || offset > data.Length)
                    throw new InvalidDataException($"LAD metadata table at slot 0x{slot:X} is outside the file.");
            }
        }

        private static readonly int[] Slots =
        [
            LadBuild.SlotDefaultValueData,
            LadBuild.SlotStrings,
            LadBuild.SlotFieldMarshaledSizes,
            LadBuild.SlotExportedTypes,
            LadBuild.SlotUnresolvedCallTypes,
            LadBuild.SlotFieldRefs,
            LadBuild.SlotGenericConstraints,
            LadBuild.SlotAttributeRanges,
            LadBuild.SlotTypeDefinitions,
            LadBuild.SlotFields,
            LadBuild.SlotInterfaces,
            LadBuild.SlotEvents,
            LadBuild.SlotProperties,
            LadBuild.SlotImages,
            LadBuild.SlotMethods,
            LadBuild.SlotAttributeData,
            LadBuild.SlotParameterDefaultValues,
            LadBuild.SlotFieldDefaultValues,
            LadBuild.SlotVTableMethods,
            LadBuild.SlotParameters,
            LadBuild.SlotGenericParameters,
            LadBuild.SlotAssemblies,
            LadBuild.SlotInterfaceOffsets,
            LadBuild.SlotGenericContainers,
            LadBuild.SlotNestedTypes,
            LadBuild.SlotStringLiteralIndex,
            LadBuild.SlotUnresolvedCallRanges,
            LadBuild.SlotStringLiteralData,
            LadBuild.SlotWindowsRuntimeStrings,
            LadBuild.SlotWindowsRuntimeTypes,
        ];

        // The decoded neighboring offsets delimit each table, including constraints and attributes.
        private int SectionSize(int slot)
        {
            var offset = SectionOffset(slot);
            var end = data.Length;
            foreach (var other in Slots)
            {
                var otherOffset = SectionOffset(other);
                if (otherOffset > offset && otherOffset < end)
                    end = otherOffset;
            }
            return end - offset;
        }

        private ImmutableArray<T> Records<T>(int offset, int byteCount, int stride, Func<ReadOnlySpan<byte>, T> read)
        {
            var count = byteCount / stride;
            if (offset < 0 || byteCount < 0 || byteCount % stride != 0 || offset + (long)byteCount > data.Length)
                throw new InvalidDataException($"LAD metadata table at 0x{offset:X} is outside the input.");
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = read(data.AsSpan(offset + i * stride, stride));
            return ImmutableCollectionsMarshal.AsImmutableArray(result);
        }

        private ImmutableArray<T> Records<T>(int slot, int stride, Func<ReadOnlySpan<byte>, T> read)
        {
            return Records(SectionOffset(slot), SectionSize(slot), stride, read);
        }

        private void ReadMetadata()
        {
            Metadata.Images = Records(LadBuild.SlotImages, 0x28, LadRecords.ReadImageDefinition);
            Metadata.Assemblies = Records(LadBuild.SlotAssemblies, 0x40, LadRecords.ReadAssemblyDefinition);

            Metadata.Types = Records(LadBuild.SlotTypeDefinitions, 0x58, LadRecords.ReadTypeDefinition);
            Metadata.Fields = Records(LadBuild.SlotFields, 0xC, LadRecords.ReadFieldDefinition);
            Metadata.Methods = Records(LadBuild.SlotMethods, 0x20, LadRecords.ReadMethodDefinition);
            Metadata.Params = Records(LadBuild.SlotParameters, 0xC, LadRecords.ReadParameterDefinition);
            Metadata.Properties = Records(LadBuild.SlotProperties, 0x14, LadRecords.ReadPropertyDefinition);
            Metadata.Events = Records(LadBuild.SlotEvents, 0x18, LadRecords.ReadEventDefinition);
            Metadata.GenericContainers = Records(LadBuild.SlotGenericContainers, 0x10, LadRecords.ReadGenericContainer);
            Metadata.GenericParameters = Records(LadBuild.SlotGenericParameters, 0x10, LadRecords.ReadGenericParameter);
            Metadata.GenericConstraintIndices = Records(LadBuild.SlotGenericConstraints, 4, static d => new TypeIndex(LadRecords.I32(d, 0)));
            Metadata.FieldDefaultValues = Records(LadBuild.SlotFieldDefaultValues, 0xC, LadRecords.ReadFieldDefaultValue);
            Metadata.ParameterDefaultValues = Records(LadBuild.SlotParameterDefaultValues, 0xC, LadRecords.ReadParameterDefaultValue);

            // Index tables are u32 streams; interface offsets are {typeIndex, offset} pairs (0x2472F30).
            Metadata.NestedTypeIndices = Records(LadBuild.SlotNestedTypes, 4, static d => LadRecords.I32(d, 0));
            // v27-style usages: kind = entry >> 29, index = (entry >> 1) & 0x0FFFFFFF (0x2472788).
            Metadata.VTableMethodIndices = Records(LadBuild.SlotVTableMethods, 4, static d => LadRecords.U32(d, 0));
            Metadata.InterfaceUsageIndices = Records(LadBuild.SlotInterfaces, 4, static d => new TypeIndex(LadRecords.I32(d, 0)));
            Metadata.InterfaceOffsets = Records(
                LadBuild.SlotInterfaceOffsets,
                8,
                static d => new Il2CppInterfaceOffsetPair { InterfaceTypeIndex = LadRecords.I32(d, 0), Offset = LadRecords.I32(d, 4) }
            );

            Metadata.FieldRefs = Records(
                LadBuild.SlotFieldRefs,
                8,
                static d => new Il2CppFieldRef { TypeIndex = LadRecords.I32(d, 0), FieldIndex = LadRecords.I32(d, 4) }
            );
            Metadata.TypeInlineArrays = [];
            Metadata.AttributeTypeRanges = [];
            Metadata.AttributeTypeIndices = [];
            Metadata.AttributeDataRanges = Metadata.ReadVersionedObjectArray<Il2CppCustomAttributeDataRange>(
                SectionOffset(LadBuild.SlotAttributeRanges),
                SectionSize(LadBuild.SlotAttributeRanges) / 8
            );

            ReadLiterals();
            InternStrings();
        }

        /// <summary>
        /// String literals are stored as {length ^ 0x1BF52, dataOffset ^ 0x1BF52} pairs (usage resolver
        /// case 5 at 0x2472594) into a packed byte stream; offsets are cumulative.
        /// </summary>
        private void ReadLiterals()
        {
            var indexOffset = SectionOffset(LadBuild.SlotStringLiteralIndex);
            var dataOffset = SectionOffset(LadBuild.SlotStringLiteralData);
            var count = SectionSize(LadBuild.SlotStringLiteralIndex) / 8;
            if (dataOffset + (long)SectionSize(LadBuild.SlotStringLiteralData) > data.Length)
                throw new InvalidDataException("LAD string literal data is outside the input.");

            var literals = new string[count];
            var blobSize = SectionSize(LadBuild.SlotStringLiteralData);
            for (var i = 0; i < count; i++)
            {
                // Plain XOR: literal pairs have no -1 sentinels.
                var length = unchecked((int)(LadRecords.U32(data, indexOffset + i * 8) ^ LadBuild.StringLiteralKey));
                var offset = unchecked((int)(LadRecords.U32(data, indexOffset + i * 8 + 4) ^ LadBuild.StringLiteralKey));
                if (length < 0 || offset < 0 || offset + (long)length > blobSize)
                    throw new InvalidDataException($"LAD string literal {i} is outside the data blob.");
                literals[i] = Encoding.UTF8.GetString(data, dataOffset + offset, length);
            }
            Metadata.StringLiterals = literals;
        }

        private void InternStrings()
        {
            void Intern(int index)
            {
                if (!Metadata.Strings.ContainsKey(index))
                    Metadata.Strings.Add(index, DecodeString(index));
            }

            foreach (var t in Metadata.Types)
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
            foreach (var g in Metadata.GenericParameters)
                Intern(g.NameIndex);
            foreach (var i in Metadata.Images)
                Intern(i.NameIndex);
            foreach (var a in Metadata.Assemblies)
            {
                Intern(a.Aname.NameIndex);
                Intern(a.Aname.CultureIndex);
            }
        }

        private string DecodeString(int index)
        {
            if (index == -1)
                return "";
            var size = SectionSize(LadBuild.SlotStrings);
            if ((uint)index >= size)
                throw new InvalidDataException("LAD string index is outside the string heap.");
            var start = stringDataOffset + index;
            var end = Array.IndexOf(data, (byte)0, start, size - index);
            if (end < 0)
                throw new InvalidDataException("LAD string has no terminator in the string heap.");
            return Encoding.UTF8.GetString(data, start, end - start);
        }

        /// <summary>Called after the fork's registration structures have been decoded from the binary.</summary>
        internal void ReadBinary(Il2CppBinary il2CppBinary)
        {
            binary = il2CppBinary;
            BuildUsages();
        }

        // 0x2472594 replaces aligned, odd v27-style tokens in writable .data with runtime pointers.
        // hdr+0xE8 holds unresolved-call signatures (0x247295C), not usages.
        private void BuildUsages()
        {
            foreach (var section in binary.Image.GetSections().Where(static s => s.Name == ".data"))
            {
                var words = binary.Image.ReadArray<ulong>(section.ImageStart, section.ImageLength / 8);
                for (var i = 0; i < words.Length; i++)
                {
                    var word = words[i];
                    if (word > uint.MaxValue || (word & 1) == 0)
                        continue;
                    var address = section.VirtualStart + (ulong)i * 8;
                    // Il2CppType flag words can look like tokens (+8 at 0xA189F00 is 0x20020001).
                    if (binary.TypeReferenceIndicesByAddress.ContainsKey(address - 8))
                        continue;
                    var kind = (MetadataUsageType)(word >> 29);
                    var index = (int)((word >> 1) & 0xFFFFFFF);
                    var count = kind switch
                    {
                        MetadataUsageType.Type or MetadataUsageType.TypeInfo => binary.TypeReferences.Length,
                        MetadataUsageType.MethodDef => Metadata.Methods.Length,
                        MetadataUsageType.MethodRef => binary.MethodSpecs.Length,
                        MetadataUsageType.FieldInfo => Metadata.FieldRefs.Length,
                        MetadataUsageType.StringLiteral => Metadata.StringLiterals.Length,
                        _ => 0,
                    };
                    if (index < count)
                        usages.Add(new MetadataUsage(kind, index, address));
                }
            }
        }

        public override List<MetadataUsage> Usages => usages;

        public override int AttributeDataOffset => SectionOffset(LadBuild.SlotAttributeData);

        public override int VTableSlotSize => 16; // 0x2471EF0 allocates 0x120 + 0x10 * vtableCount.

        // Definitions retain the stock size records; no initialized generic layout cache is shipped.
        public override Il2CppTypeDefinitionSizes LayoutSizes(int group) => binary.TypeDefinitionSizes[group];

        public override uint LayoutFieldOffset(int group, int local) => 0;

        public override string StorageBase(uint tag) => tag == 1 ? "thread_static_fields" : "klass->static_fields";

        public override int FieldStorageTag(FieldInfo field) => field.IsThreadStatic ? 1 : 0;

        public override TypeInfo ResolveGenericType(TypeModel model, Il2CppType type)
        {
            var image = binary.Image;
            // Il2CppGenericClass+0x00 points to a compact {u32 index, u32 typeBits} record (0x2472398 / 0x2472B40);
            // context.classInst is at +0x08.
            var generic = type.Data.GenericClass;
            var compact = image.ReadMappedUInt64(generic);
            var definition = model.TypesByDefinitionIndex[image.ReadMappedUInt32(compact)];
            var instance = image.ReadMappedVersionedObject<Il2CppGenericInst>(image.ReadMappedUInt64(generic + 8));
            return definition.MakeGenericType(model.ResolveGenericArguments(instance));
        }

        public override int FieldRvaSize(Il2CppType type, Il2CppBinary il2CppBinary) =>
            type.Type switch
            {
                Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN or Il2CppTypeEnum.IL2CPP_TYPE_I1 or Il2CppTypeEnum.IL2CPP_TYPE_U1 => 1,
                Il2CppTypeEnum.IL2CPP_TYPE_CHAR or Il2CppTypeEnum.IL2CPP_TYPE_I2 or Il2CppTypeEnum.IL2CPP_TYPE_U2 => 2,
                Il2CppTypeEnum.IL2CPP_TYPE_I4 or Il2CppTypeEnum.IL2CPP_TYPE_U4 or Il2CppTypeEnum.IL2CPP_TYPE_R4 => 4,
                Il2CppTypeEnum.IL2CPP_TYPE_I8 or Il2CppTypeEnum.IL2CPP_TYPE_U8 or Il2CppTypeEnum.IL2CPP_TYPE_R8 => 8,
                Il2CppTypeEnum.IL2CPP_TYPE_I or Il2CppTypeEnum.IL2CPP_TYPE_U => il2CppBinary.Image.Bits / 8,
                Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE => checked((int)il2CppBinary.TypeDefinitionSizes[type.Data.KlassIndex].InstanceSize - 16),
                _ => 0,
            };

        public override (ulong Address, object Value) DecodeDefault(int typeIndex, int dataIndex, Il2CppBinary il2CppBinary)
        {
            if (dataIndex == -1)
                return (0, null);

            if ((uint)typeIndex >= il2CppBinary.TypeReferences.Length || (uint)dataIndex >= SectionSize(LadBuild.SlotDefaultValueData))
                throw new InvalidDataException("LAD default value contains an invalid type or data index.");

            var address = checked(defaultValueDataOffset + dataIndex);
            var offset = address;
            var kind = il2CppBinary.TypeReferences[typeIndex].Type;
            object value = kind switch
            {
                Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN => data[offset] != 0,
                Il2CppTypeEnum.IL2CPP_TYPE_I1 => unchecked((sbyte)data[offset]),
                Il2CppTypeEnum.IL2CPP_TYPE_U1 => data[offset],
                Il2CppTypeEnum.IL2CPP_TYPE_CHAR => (char)LadRecords.U16(data, offset),
                Il2CppTypeEnum.IL2CPP_TYPE_I2 => LadRecords.I16(data, offset),
                Il2CppTypeEnum.IL2CPP_TYPE_U2 => LadRecords.U16(data, offset),
                Il2CppTypeEnum.IL2CPP_TYPE_I4 => Metadata.ReadCompressedInt32(offset),
                Il2CppTypeEnum.IL2CPP_TYPE_U4 => Metadata.ReadCompressedUInt32(offset),
                Il2CppTypeEnum.IL2CPP_TYPE_I8 => unchecked((long)ReadU64(offset)),
                Il2CppTypeEnum.IL2CPP_TYPE_U8 => ReadU64(offset),
                Il2CppTypeEnum.IL2CPP_TYPE_R4 => BitConverter.Int32BitsToSingle(LadRecords.I32(data, offset)),
                Il2CppTypeEnum.IL2CPP_TYPE_R8 => BitConverter.Int64BitsToDouble(unchecked((long)ReadU64(offset))),
                Il2CppTypeEnum.IL2CPP_TYPE_STRING => DecodeDefaultString(offset),
                _ => null,
            };
            return ((ulong)address, value);
        }

        private ulong ReadU64(int offset) => BitConverter.ToUInt64(data, offset);

        private string DecodeDefaultString(int offset)
        {
            var length = Metadata.ReadCompressedInt32(offset);
            offset = checked((int)Metadata.Position);
            if (length == -1)
                return null;
            var end = defaultValueDataOffset + SectionSize(LadBuild.SlotDefaultValueData);
            if (length < 0 || offset + (long)length > end)
                throw new InvalidDataException("LAD default string is outside the default-value blob.");
            return Encoding.UTF8.GetString(data, offset, length);
        }
    }
}
