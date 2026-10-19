using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInspector.Next;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;
using Il2CppInspector.Reflection;
using VersionedSerialization;

namespace Il2CppInspector
{
    /// <summary>
    /// C# adapter for the 未定事件簿 (TOT) 6.1.0 Android build.
    ///
    /// The decrypted global-metadata.dat is stock IL2CPP v24.1 at the record level, but its 0x158-byte
    /// header is packer-permuted: sections are stored as (offset, size) pairs at non-stock slots, ~23
    /// slots are scrambled key material for the inner layer, and the stringLiteral index table stores
    /// {dataIndex, length} rather than stock {length, dataIndex}. The standard header reader therefore
    /// cannot parse it, so this adapter reads the permuted slots directly and populates the stock
    /// Metadata tables (the ZzzMorax construction pattern).
    /// </summary>
    internal sealed partial class TotMetadata : Plugins.GameMetadataAdapter
    {
        // Header slot byte offsets of each section's (offset, size) pair. Slot 0x24 is a lone u32 count.
        private const int SlotStringLiteralCount = 0x24;
        private const int SlotGenericContainers = 0x28;
        private const int SlotNestedTypes = 0x30;
        private const int SlotInterfaces = 0x38;
        private const int SlotVTableMethods = 0x40;
        private const int SlotInterfaceOffsets = 0x48;
        private const int SlotTypeDefinitions = 0x50;
        private const int SlotUnresolved = 0x58;
        private const int SlotImages = 0x70;
        private const int SlotAssemblies = 0x78;
        private const int SlotFields = 0x80;
        private const int SlotGenericParameters = 0x88;
        private const int SlotDefaultValueData = 0x90;
        private const int SlotFieldMarshaledSizes = 0x98;
        private const int SlotReferencedAssemblies = 0xA0;
        private const int SlotAttributesInfo = 0xA8;
        private const int SlotAttributeTypes = 0xB0;
        private const int SlotExportedTypeDefinitions = 0xD0;
        private const int SlotParameters = 0xE0;
        private const int SlotGenericParameterConstraints = 0xE8;
        private const int SlotMetadataUsagePairs = 0xF8;
        private const int SlotFieldRefs = 0x110;
        private const int SlotEvents = 0x118;
        private const int SlotProperties = 0x120;
        private const int SlotMethods = 0x128;
        private const int SlotParameterDefaultValues = 0x130;
        private const int SlotFieldDefaultValues = 0x138;
        private const int SlotMetadataUsageLists = 0x150;

        // The string (names) table offset is baked into libunity's string getter, not stored in the header.
        private const int StringTableOffset = 0xA15068;

        internal readonly Metadata Metadata;
        private readonly byte[] data;
        private readonly List<MetadataUsage> usages = [];
        private Il2CppBinary binary;

        internal TotMetadata(byte[] decrypted, EventHandler<string> status, Plugins.GamePlugin plugin)
            : base(plugin)
        {
            data = decrypted;
            Metadata = Metadata.CreateForPlugin(decrypted, this, MetadataVersions.V241, status);
            status?.Invoke(this, "Decoding Tears of Themis 6.1.0 metadata in C#");
            ReadMetadata();
        }

        private int SectionOffset(int slot) => TotRecords.I32(data, slot);

        private int SectionSize(int slot) => TotRecords.I32(data, slot + 4);

        private int SectionCount(int slot, int stride) => SectionSize(slot) / stride;

        private static ImmutableArray<T> Records<T>(byte[] bytes, int offset, int count, int stride, Func<ReadOnlySpan<byte>, T> read)
        {
            if (offset < 0 || count < 0 || offset > bytes.Length - (long)count * stride)
            {
                throw new InvalidDataException($"TOT metadata table outside input: offset 0x{offset:X}, count {count}.");
            }

            var result = new T[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = read(bytes.AsSpan(offset + i * stride, stride));
            }

            return ImmutableCollectionsMarshal.AsImmutableArray(result);
        }

        private void ReadMetadata()
        {
            Metadata.Images = Records(data, SectionOffset(SlotImages), SectionCount(SlotImages, 40), 40, TotRecords.ReadImageDefinition);
            Metadata.Assemblies = Records(data, SectionOffset(SlotAssemblies), SectionCount(SlotAssemblies, 64), 64, TotRecords.ReadAssemblyDefinition);
            Metadata.Types = Records(data, SectionOffset(SlotTypeDefinitions), SectionCount(SlotTypeDefinitions, 100), 100, TotRecords.ReadTypeDefinition);
            Metadata.Methods = Records(data, SectionOffset(SlotMethods), SectionCount(SlotMethods, 52), 52, TotRecords.ReadMethodDefinition);
            Metadata.Fields = Records(data, SectionOffset(SlotFields), SectionCount(SlotFields, 12), 12, TotRecords.ReadFieldDefinition);
            Metadata.Params = Records(data, SectionOffset(SlotParameters), SectionCount(SlotParameters, 12), 12, TotRecords.ReadParameterDefinition);
            Metadata.Properties = Records(data, SectionOffset(SlotProperties), SectionCount(SlotProperties, 20), 20, TotRecords.ReadPropertyDefinition);
            Metadata.Events = Records(data, SectionOffset(SlotEvents), SectionCount(SlotEvents, 24), 24, TotRecords.ReadEventDefinition);
            Metadata.GenericContainers = Records(data, SectionOffset(SlotGenericContainers), SectionCount(SlotGenericContainers, 16), 16, TotRecords.ReadGenericContainer);
            Metadata.GenericParameters = Records(data, SectionOffset(SlotGenericParameters), SectionCount(SlotGenericParameters, 16), 16, TotRecords.ReadGenericParameter);
            Metadata.InterfaceOffsets = Records(data, SectionOffset(SlotInterfaceOffsets), SectionCount(SlotInterfaceOffsets, 8), 8, TotRecords.ReadInterfaceOffset);
            Metadata.FieldRefs = Records(data, SectionOffset(SlotFieldRefs), SectionCount(SlotFieldRefs, 8), 8, TotRecords.ReadFieldRef);
            Metadata.FieldDefaultValues = Records(data, SectionOffset(SlotFieldDefaultValues), SectionCount(SlotFieldDefaultValues, 12), 12, TotRecords.ReadFieldDefaultValue);
            Metadata.ParameterDefaultValues = Records(data, SectionOffset(SlotParameterDefaultValues), SectionCount(SlotParameterDefaultValues, 12), 12, TotRecords.ReadParameterDefaultValue);
            Metadata.AttributeTypeRanges = Records(data, SectionOffset(SlotAttributesInfo), SectionCount(SlotAttributesInfo, 12), 12, TotRecords.ReadCustomAttributeTypeRange);
            // MetadataUsageList/Pair expose read-only properties, so read them through the stock
            // versioned reader (their v24.1 layout matches the repo model exactly).
            Metadata.MetadataUsageLists = Reader.ReadVersionedObjectArray<Il2CppMetadataUsageList>(
                data,
                SectionCount(SlotMetadataUsageLists, 8),
                MetadataVersions.V241,
                SectionOffset(SlotMetadataUsageLists)
            );
            Metadata.MetadataUsagePairs = Reader.ReadVersionedObjectArray<Il2CppMetadataUsagePair>(
                data,
                SectionCount(SlotMetadataUsagePairs, 8),
                MetadataVersions.V241,
                SectionOffset(SlotMetadataUsagePairs)
            );

            Metadata.InterfaceUsageIndices = Records(data, SectionOffset(SlotInterfaces), SectionCount(SlotInterfaces, 4), 4, d => new TypeIndex(TotRecords.I32(d, 0)));
            Metadata.NestedTypeIndices = Records(data, SectionOffset(SlotNestedTypes), SectionCount(SlotNestedTypes, 4), 4, d => TotRecords.I32(d, 0));
            Metadata.AttributeTypeIndices = Records(data, SectionOffset(SlotAttributeTypes), SectionCount(SlotAttributeTypes, 4), 4, d => TotRecords.I32(d, 0));
            Metadata.GenericConstraintIndices = Records(
                data,
                SectionOffset(SlotGenericParameterConstraints),
                SectionCount(SlotGenericParameterConstraints, 4),
                4,
                d => new TypeIndex(TotRecords.I32(d, 0))
            );
            Metadata.VTableMethodIndices = Records(data, SectionOffset(SlotVTableMethods), SectionCount(SlotVTableMethods, 4), 4, d => TotRecords.U32(d, 0));

            // The unused sections are left empty rather than throwing: the stock pipeline never needs them.
            Metadata.AttributeDataRanges = [];
            Metadata.TypeInlineArrays = [];

            ReadLiterals();
            InternStrings();
            ReadAssemblyPublicKeys();
        }

        /// <summary>
        /// The packer does not store v24.1 escaped-hex public keys in the string table (the recorded
        /// PublicKeyIndex points at raw bytes), so use each assembly's 8-byte public key token. This is
        /// enough for the assembly-shim writer, which only needs a stable blob per assembly.
        /// </summary>
        private void ReadAssemblyPublicKeys()
        {
            foreach (var assembly in Metadata.Assemblies)
            {
                if ((assembly.Aname.Flags & System.Reflection.AssemblyNameFlags.PublicKey) == 0)
                {
                    continue;
                }

                var token = new byte[8];
                for (var i = 0; i < 8; i++)
                {
                    token[i] = assembly.Aname.PublicKeyToken[i];
                }

                Metadata.AssemblyPublicKeys[assembly.Aname.PublicKeyIndex] = token;
            }
        }

        /// <summary>
        /// stringLiteralData blob lives at 0x158; its index table at 0xEBC7C stores {dataIndex, length}
        /// (swapped relative to stock). The literal bytes were already decrypted by the inner layer.
        /// </summary>
        private void ReadLiterals()
        {
            // Slot 0x24 stores the index table's byte size (36670 * 8), not the literal count.
            var count = TotRecords.U32(data, SlotStringLiteralCount) / 8;
            var indexOffset = TotMetadataDecryptor.LiteralIndexOffset;
            if (indexOffset + (long)count * 8 > data.Length)
            {
                throw new InvalidDataException("TOT stringLiteral index table is out of range.");
            }

            var literals = new string[count];
            for (var i = 0; i < count; i++)
            {
                var dataIndex = TotRecords.I32(data, indexOffset + i * 8);
                var length = TotRecords.I32(data, indexOffset + i * 8 + 4);
                literals[i] = Encoding.UTF8.GetString(data, TotMetadataDecryptor.LiteralDataOffset + dataIndex, length);
            }

            Metadata.StringLiterals = literals;
        }

        private void InternStrings()
        {
            void Intern(int index)
            {
                if (!Metadata.Strings.ContainsKey(index))
                {
                    Metadata.Strings.Add(index, DecodeString(index));
                }
            }

            foreach (var t in Metadata.Types)
            {
                Intern(t.NameIndex);
                Intern(t.NamespaceIndex);
            }

            foreach (var f in Metadata.Fields)
            {
                Intern(f.NameIndex);
            }

            foreach (var m in Metadata.Methods)
            {
                Intern(m.NameIndex);
            }

            foreach (var p in Metadata.Params)
            {
                Intern(p.NameIndex);
            }

            foreach (var p in Metadata.Properties)
            {
                Intern(p.NameIndex);
            }

            foreach (var e in Metadata.Events)
            {
                Intern(e.NameIndex);
            }

            foreach (var g in Metadata.GenericParameters)
            {
                Intern(g.NameIndex);
            }

            foreach (var i in Metadata.Images)
            {
                Intern(i.NameIndex);
            }

            foreach (var a in Metadata.Assemblies)
            {
                Intern(a.Aname.NameIndex);
                Intern(a.Aname.CultureIndex);
            }
        }

        private string DecodeString(int index)
        {
            if (index < 0)
            {
                return "";
            }

            var start = StringTableOffset + index;
            if (start < 0 || start >= data.Length)
            {
                return "";
            }

            var end = Array.IndexOf(data, (byte)0, start);
            if (end < 0)
            {
                end = data.Length;
            }

            return Encoding.UTF8.GetString(data, start, end - start);
        }

        // ---------------------------------------------------------- binary phase

        /// <summary>Called after the stock binary registration has been discovered.</summary>
        internal void ReadBinary(Il2CppBinary il2CppBinary)
        {
            binary = il2CppBinary;
            BuildUsages();
        }

        /// <summary>
        /// Reconstructs the metadata-usage list the way the stock loader would: destinations are the
        /// encoded indices from metadataUsagePairs, addresses come from MetadataRegistration.metadataUsages.
        /// </summary>
        private void BuildUsages()
        {
            if (Metadata.MetadataUsageLists.IsDefaultOrEmpty || binary?.MetadataRegistration == null)
            {
                return;
            }

            var destinations = new Dictionary<uint, uint>();
            foreach (var list in Metadata.MetadataUsageLists)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var pair = Metadata.MetadataUsagePairs[(int)(list.Start + i)];
                    destinations.TryAdd(pair.DestinationIndex, pair.EncodedSourceIndex);
                }
            }

            if (destinations.Count == 0)
            {
                return;
            }

            var count = (int)destinations.Keys.Max() + 1;
            var addresses = binary.Image.ReadMappedUWordArray(binary.MetadataRegistration.MetadataUsages, count);
            foreach (var (destination, encoded) in destinations)
            {
                var usageType = (MetadataUsageType)((encoded & 0xE0000000) >> 29);
                var sourceIndex = (int)(encoded & 0x1FFFFFFF);
                usages.Add(new MetadataUsage(usageType, sourceIndex, addresses[destination]));
            }
        }

        public override List<MetadataUsage> Usages => usages;

        public override int VTableSlotSize => 8;

        // TOT uses the stock shared-generic layout, so no special type layout group is required.
        public override Il2CppTypeDefinitionSizes LayoutSizes(int group) => binary != null && group >= 0 && group < binary.TypeDefinitionSizes.Length ? binary.TypeDefinitionSizes[group] : default;

        public override uint LayoutFieldOffset(int group, int local) => 0;

        public override string StorageBase(uint tag) => null;

        public override int FieldStorageTag(FieldInfo field) => 0;

        // Mirrors the stock (no-adapter) generic-instance resolution in TypeModel.resolveTypeReference.
        public override TypeInfo ResolveGenericType(TypeModel model, Il2CppType type)
        {
            if (binary == null)
            {
                return null;
            }

            var generic = binary.Image.ReadMappedVersionedObject<Il2CppGenericClass>(type.Data.GenericClass);
            var definition = model.TypesByDefinitionIndex[generic.TypeDefinitionIndex];
            if (definition == null)
            {
                return null;
            }

            var instance = binary.Image.ReadMappedVersionedObject<Il2CppGenericInst>(generic.Context.ClassInst);
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
            if (dataIndex < 0)
            {
                return (0, null);
            }

            var offset = checked(SectionOffset(SlotDefaultValueData) + dataIndex);
            var kind = il2CppBinary.TypeReferences[typeIndex].Type;
            object value = kind switch
            {
                Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN => data[offset] != 0,
                Il2CppTypeEnum.IL2CPP_TYPE_I1 => unchecked((sbyte)data[offset]),
                Il2CppTypeEnum.IL2CPP_TYPE_U1 => data[offset],
                Il2CppTypeEnum.IL2CPP_TYPE_CHAR => (char)TotRecords.U16(data, offset),
                Il2CppTypeEnum.IL2CPP_TYPE_I2 => TotRecords.I16(data, offset),
                Il2CppTypeEnum.IL2CPP_TYPE_U2 => TotRecords.U16(data, offset),
                Il2CppTypeEnum.IL2CPP_TYPE_I4 => TotRecords.I32(data, offset),
                Il2CppTypeEnum.IL2CPP_TYPE_U4 => TotRecords.U32(data, offset),
                Il2CppTypeEnum.IL2CPP_TYPE_I8 => unchecked((long)ReadU64(offset)),
                Il2CppTypeEnum.IL2CPP_TYPE_U8 => ReadU64(offset),
                Il2CppTypeEnum.IL2CPP_TYPE_R4 => BitConverter.Int32BitsToSingle(TotRecords.I32(data, offset)),
                Il2CppTypeEnum.IL2CPP_TYPE_R8 => BitConverter.Int64BitsToDouble(unchecked((long)ReadU64(offset))),
                Il2CppTypeEnum.IL2CPP_TYPE_STRING => DecodeDefaultString(offset),
                _ => null,
            };
            return ((ulong)offset, value);
        }

        private ulong ReadU64(int offset) => BitConverter.ToUInt64(data, offset);

        private string DecodeDefaultString(int offset)
        {
            var length = TotRecords.I32(data, offset);
            if (length == -1)
            {
                return null;
            }

            if (length < 0 || length > 1 << 20 || offset + 4 + length > data.Length)
            {
                throw new InvalidDataException("Invalid TOT default string length.");
            }

            return Encoding.UTF8.GetString(data, offset + 4, length);
        }
    }
}
