using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInspector.Next;
using Il2CppInspector.Next.Metadata;
using VersionedSerialization;

namespace Il2CppInspector
{
    /// <summary>Static C# adapter for the IDA-verified Genshin 7.1.0 Windows MORAX build.</summary>
    internal sealed partial class GenshinMorax : Plugins.GameMetadataAdapter
    {
        public override string StorageBase(uint tag) =>
            tag switch
            {
                0 => "klass->static_fields (+0x78)",
                1 => "thread static storage",
                2 => "MetadataRegistration->globalStaticStorage (+0x58)",
                4 => "*MetadataRegistration->globalStaticStorageSlot (+0x00)",
                _ => "unknown",
            };

        public override int VTableSlotSize => 8;
        private const int Payload = 0x210;
        internal readonly IFileFormatStream Image;
        private readonly byte[] global;
        private readonly byte[] startup;
        private readonly Dictionary<int, int> slots = [];
        internal readonly Metadata Metadata;
        internal readonly ulong CodeRegistrationAddress;
        internal readonly ulong MetadataRegistrationAddress;
        internal readonly ulong UsageAddress;
        internal readonly ulong RegistrationAddress;
        internal bool EnableEnumSharing { get; }
        private readonly Section[] executableSections;

        // 24.1 selects global method/Invoker arrays and image-scoped CA tokens.
        // Every physical record is read explicitly; this is not the original player version.
        private static readonly StructVersion NominalMetadataVersion = MetadataVersions.V241;

        internal static ushort U16(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(o, 2));

        internal static short I16(ReadOnlySpan<byte> d, int o) => unchecked((short)U16(d, o));

        internal static uint U32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(o, 4));

        internal static int I32(ReadOnlySpan<byte> d, int o) => unchecked((int)U32(d, o));

        internal static ulong U64(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt64LittleEndian(d.Slice(o, 8));

        internal static int Index16(ushort v) => v == ushort.MaxValue ? -1 : v;

        private int Slot(int o) => slots[o];

        private int Base(int o) => checked(Payload + Slot(o));

        // Physical table boundaries avoid the padded size slots in this build.
        private int TableCount(int startSlot, int endSlot, int stride)
        {
            var length = checked(Slot(endSlot) - Slot(startSlot));
            if (length < 0 || length % stride != 0)
                throw new InvalidDataException("Genshin table boundaries do not match the record stride.");
            return length / stride;
        }

        private delegate T RecordReader<T>(ReadOnlySpan<byte> d, int index);

        private static ImmutableArray<T> Records<T>(byte[] bytes, int offset, int count, int stride, RecordReader<T> read)
        {
            CheckRange(bytes, offset, checked(count * stride));
            var result = new T[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = read(bytes.AsSpan(offset + i * stride, stride), i);
            }

            return ImmutableCollectionsMarshal.AsImmutableArray(result);
        }

        private static void CheckRange(byte[] bytes, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > bytes.Length - length)
            {
                throw new InvalidDataException($"Genshin table outside input: offset 0x{offset:X}, length 0x{length:X}.");
            }
        }

        private GenshinMorax(IFileFormatStream image, byte[] globalData, byte[] startupData, EventHandler<string> status, Plugins.GamePlugin plugin)
            : base(plugin)
        {
            Image = image;
            global = globalData;
            startup = startupData;
            if (image is not PEReader || image.Bits != 64 || image.Arch != "x64")
            {
                throw new NotSupportedException("Genshin currently supports the 7.1.0 Windows x64 build.");
            }

            if (!plugin.Matches(global))
            {
                throw new InvalidDataException("Missing MHY metadata container signature.");
            }

            executableSections = image.GetSections().Where(s => s.IsExec).ToArray();
            var addresses = DiscoverRegistration(image);
            RegistrationAddress = addresses[0];
            CodeRegistrationAddress = addresses[1];
            MetadataRegistrationAddress = addresses[2];
            UsageAddress = addresses[3];
            EnableEnumSharing = image.ReadMappedBytes(addresses[4], 1)[0] != 0;
            // The effective header is the 0x210-byte "MHY\0" block embedded in YuanShen.exe,
            // not the decoy block at the start of global-metadata.dat.
            var header = image.ReadMappedBytes(addresses[5], HeaderCiphers.Max(c => c.Offset) + sizeof(uint));
            foreach (var (offset, kind, key) in HeaderCiphers)
            {
                var raw = U32(header, offset);
                slots.Add(
                    offset,
                    kind switch
                    {
                        0 => unchecked((int)(raw ^ key)),
                        1 => unchecked((int)(raw + key)),
                        2 => unchecked((int)(raw - key)),
                        3 => unchecked((int)((raw >> 3) ^ key)),
                        4 => unchecked((int)((raw >> 4) ^ key)),
                        _ => throw new InvalidDataException("Unknown header transform."),
                    }
                );
            }
            // MHY is shared by several games. Reject unknown layouts before allocating table arrays.
            ValidateLayout();

            Metadata = Metadata.CreateForPlugin(global, this, NominalMetadataVersion, status);
            status?.Invoke(this, "Decoding Genshin 7.1.0 metadata in C#");
            ReadMetadata();
        }

        internal static Il2CppInspector Load(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status, Plugins.GamePlugin plugin)
        {
            var adapter = new GenshinMorax(image, global, startup, status, plugin);
            var binary = new Il2CppBinaryX64(image, status);
            adapter.ReadBinary(binary);
            return new Il2CppInspector(binary, adapter.Metadata);
        }

        // Payload-relative table offsets verified against the reports. Any mismatch means this is a
        // different MORAX build whose slot keys and table layout do not apply.
        private void ValidateLayout()
        {
            if (
                Slot(0xDC) != 0x48F3FD8
                || Slot(0x1E0) != 0x23F8770
                || TableCount(0x1E0, 0x1F4, 26) != 733442
                || TableCount(0xDC, 0x5C, 70) != 88904
                || Slot(0x84) != 0x35D5E8
                || Slot(0xA4) / 40 != 75
                || global.Length != 82726068
                || startup.Length != 4060728
            )
                throw new NotSupportedException("Unsupported Genshin MORAX build: permutations and keys vary per build.");
        }

        private static ulong[] DiscoverRegistration(IFileFormatStream image)
        {
            // Resolve the five RIP-relative targets at this build's verified registration RVA.
            var start = image.ImageBase + 0x2F3730;
            if (!image.TryMapVATR(start, out _))
                throw new NotSupportedException("Missing Genshin registration block.");
            var bytes = image.ReadMappedBytes(start, 70);
            var targets = new ulong[6];
            targets[0] = start;
            for (var n = 0; n < 5; n++)
            {
                var o = n * 14;
                if (bytes[o] != 0x48 || bytes[o + 1] != 0x8D || bytes[o + 2] != 0x05 || bytes[o + 7] != 0x48 || bytes[o + 8] != 0x89 || bytes[o + 9] != 0x05)
                    throw new NotSupportedException("Unsupported Genshin registration instructions.");
                targets[n + 1] = unchecked((ulong)((long)start + o + 7 + I32(bytes, o + 3)));
            }
            if (image.ReadMappedUInt32(targets[5]) != 0x0059484D || targets[3] != targets[2] + 0xA0)
                throw new InvalidDataException("Genshin registration/header mismatch.");
            return targets;
        }

        internal ImmutableArray<int> ExportedTypeIndices { get; private set; }

        private void ReadMetadata()
        {
            Metadata.Images = Records(startup, Slot(0x84), Slot(0xA4) / 40, 40, GenshinRecords.Image);
            var typeCount = 0;
            foreach (var image in Metadata.Images)
            {
                if (image.TypeStart != typeCount)
                    throw new InvalidDataException("Image type ranges are not contiguous.");
                typeCount = checked(typeCount + (int)image.TypeCount);
            }
            if (typeCount != 88902)
                throw new InvalidDataException("Unexpected real type count.");
            var types = Records(global, Base(0xDC), typeCount, 70, GenshinRecords.Type);
            Metadata.Types = types;
            var methods = Records(global, Base(0x1E0), TableCount(0x1E0, 0x1F4, 26), 26, GenshinRecords.Method).ToArray();
            for (var i = 0; i < methods.Length; i++)
                methods[i].InvokerIndex = I32(global, Base(0x200) + i * 4);
            Metadata.Methods = ImmutableCollectionsMarshal.AsImmutableArray(methods);
            var fields = types.Max(t => t.FieldCount == 0 ? 0 : checked((int)t.FieldIndex + t.FieldCount));
            var properties = types.Max(t => t.PropertyCount == 0 ? 0 : checked((int)t.PropertyIndex + t.PropertyCount));
            var events = types.Max(t => t.EventCount == 0 ? 0 : checked((int)t.EventIndex + t.EventCount));
            Metadata.Fields = Records(global, Base(0x4C), fields, 8, GenshinRecords.Field);
            Metadata.Properties = Records(global, Base(0x158), properties, 10, GenshinRecords.Property);
            Metadata.Events = Records(global, Base(0x64), events, 14, GenshinRecords.Event);
            var parameters = Metadata.Methods.Max(m => m.ParameterCount == 0 ? 0 : checked((int)m.ParameterStart + m.ParameterCount));
            Metadata.Params = Records(global, Base(0x68), parameters, 8, GenshinRecords.Parameter);
            Metadata.NestedTypeIndices = Records(global, Base(0x1B8), types.Max(t => t.NestedTypeCount == 0 ? 0 : (int)t.NestedTypeIndex + t.NestedTypeCount), 4, (d, _) => I32(d, 0));
            Metadata.InterfaceUsageIndices = Records(
                global,
                Base(0x44),
                types.Max(t => t.InterfacesCount == 0 ? 0 : (int)t.InterfacesIndex + t.InterfacesCount),
                4,
                (d, _) => new TypeIndex(I32(d, 0))
            );
            Metadata.InterfaceOffsets = Records(
                global,
                Base(0x70),
                types.Max(t => t.InterfaceOffsetsCount == 0 ? 0 : (int)t.InterfaceOffsetsStart + t.InterfaceOffsetsCount),
                6,
                (d, _) => new Il2CppInterfaceOffsetPair { InterfaceTypeIndex = I32(d, 0), Offset = I16(d, 4) }
            );
            Metadata.VTableMethodIndices = Records(global, Base(0x98), types.Max(t => t.VTableCount == 0 ? 0 : t.VTableIndex + t.VTableCount), 4, (d, _) => U32(d, 0));
            ExportedTypeIndices = Records(global, Base(0x5C), checked((int)Metadata.Images.Sum(i => (long)i.ExportedTypeCount)), 4, (d, _) => I32(d, 0));
            if (ExportedTypeIndices.Any(i => (uint)i >= types.Length))
                throw new InvalidDataException("Exported type index out of range.");
            Metadata.Assemblies = Records(startup, Slot(0x2C), Metadata.Images.Length, 16, ReadAssembly);
            var containers = types.Select(t => (int)t.GenericContainerIndex).Concat(Metadata.Methods.Select(m => (int)m.GenericContainerIndex)).Max() + 1;
            Metadata.GenericContainers = Records(global, Base(0xC8), containers, 16, GenshinRecords.Container);
            var genericParameters = Metadata.GenericContainers.Max(c => (int)c.GenericParameterStart + c.TypeArgc);
            Metadata.GenericParameters = Records(global, Base(0x174), genericParameters, 14, GenshinRecords.GenericParameter);
            var constraints = Metadata.GenericParameters.Max(p => p.ConstraintsCount == 0 ? 0 : p.ConstraintsStart + p.ConstraintsCount);
            Metadata.GenericConstraintIndices = Records(global, Base(0x3C), constraints, 4, (d, _) => new TypeIndex(I32(d, 0)));
            Metadata.FieldDefaultValues = Records(
                global,
                Base(0x164),
                TableCount(0x164, 0x174, 12),
                12,
                (d, _) =>
                    new Il2CppFieldDefaultValue
                    {
                        DataIndex = I32(d, 0),
                        FieldIndex = I32(d, 4),
                        TypeIndex = I32(d, 8),
                    }
            );
            Metadata.ParameterDefaultValues = Records(
                global,
                Base(0x1D0),
                TableCount(0x1D0, 0x188, 12),
                12,
                (d, _) =>
                    new Il2CppParameterDefaultValue
                    {
                        ParameterIndex = I32(d, 0),
                        TypeIndex = I32(d, 4),
                        DataIndex = I32(d, 8),
                    }
            );
            var attributes = checked((int)Metadata.Images.Sum(i => (long)i.CustomAttributeCount));
            Metadata.AttributeTypeRanges = Records(
                global,
                Base(0x118),
                attributes,
                8,
                (d, _) =>
                    new Il2CppCustomAttributeTypeRange
                    {
                        Token = U32(d, 0),
                        Start = (int)(U32(d, 4) & 0xFFFFFF),
                        Count = (int)(U32(d, 4) >> 24),
                    }
            );
            var attributeTypes = Metadata.AttributeTypeRanges.Max(a => a.Start + a.Count);
            Metadata.AttributeTypeIndices = Records(global, Base(0x1A8), attributeTypes, 4, (d, _) => I32(d, 0));
            Metadata.FieldRefs = Records(
                global,
                Base(0x9C),
                TableCount(0x9C, 0x128, 8),
                8,
                (d, i) =>
                {
                    var k = unchecked(0x9A864480u * (uint)i);
                    return new Il2CppFieldRef { TypeIndex = unchecked((int)(U32(d, 0) + k - 0x7A03B34Au)), FieldIndex = unchecked((int)((U32(d, 4) ^ 0x747BB38Bu) + k + 0x01C1C314u)) };
                }
            );
            Metadata.AttributeDataRanges = [];
            Metadata.MetadataUsageLists = [];
            Metadata.MetadataUsagePairs = [];
            Metadata.TypeInlineArrays = [];
            foreach (var t in types)
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
            foreach (var p in Metadata.GenericParameters)
                Intern(p.NameIndex);
            foreach (var i in Metadata.Images)
                Intern(i.NameIndex);
            foreach (var assembly in Metadata.Assemblies)
            {
                Intern(assembly.Aname.NameIndex);
                Intern(assembly.Aname.CultureIndex);
            }
            ReadLiterals();
        }

        private void Intern(int index)
        {
            if (!Metadata.Strings.ContainsKey(index))
            {
                Metadata.Strings.Add(index, DecodeString(unchecked((uint)index)));
            }
        }

        private string DecodeString(uint index)
        {
            if (index == uint.MaxValue || index >> 24 == 0)
                return "";
            var offset = (int)(index & 0xFFFFFF);
            var seed = unchecked(0xB33E40427D5668C0UL * (ulong)offset + 0x6D8C4AAB00FE8E27UL) ^ 0x30EE5B43130FE0CDUL;
            return DecodeBlocks(Base(0x150) + offset, (int)(index >> 24), seed, 0x0889EEEA326AFB36UL);
        }

        private string DecodeBlocks(int offset, int length, ulong key, ulong increment)
        {
            var rounded = checked((length + 7) & ~7);
            CheckRange(global, offset, rounded);
            Span<byte> bytes = rounded <= 512 ? stackalloc byte[rounded] : new byte[rounded];
            for (var i = 0; i < rounded; i += 8)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(i, 8), U64(global, offset + i) ^ unchecked(key + (ulong)(i / 8) * increment));
            }

            return Encoding.UTF8.GetString(bytes[..length]);
        }

        private void ReadLiterals()
        {
            var count = checked((int)(Image.ReadMappedUInt32(UsageAddress + 0x20) - 0x1024A965u));
            var literals = new string[count];
            for (var i = 0; i < count; i++)
            {
                var start = checked((int)LiteralOffset(i));
                var length = checked((int)LiteralOffset(i + 1) - start);
                if (length < 0)
                    throw new InvalidDataException("Literal offsets are not monotonic.");
                literals[i] = DecodeBlocks(Base(0x13C) + start, length, LiteralSeed(i), 0x70BA8080760AC800UL);
            }
            Metadata.StringLiterals = literals;
        }

        private uint LiteralOffset(int i)
        {
            var r = unchecked(0x76C8023DUL * ((0x9825UL * (uint)i) ^ 0x250621A3UL));
            r = unchecked((r >> 12) * 0x5E626528UL) >> 15;
            var key = unchecked((uint)r + 0x3483F478u) ^ 0xA0D4507Fu;
            return unchecked(key + U32(global, Base(0x88) + i * 4) - 0x7EAAA2FCu);
        }

        private static ulong LiteralSeed(int i) => unchecked(0x29140A6156B9041EUL * ((0x4D613E5575A12594UL * ((0x34044C3B94B40CUL * (uint)i) ^ 0x2C165A1E0D09C29EUL)) ^ 0x1B34FA773FEE107EUL));

        private Il2CppAssemblyDefinition ReadAssembly(ReadOnlySpan<byte> d, int i)
        {
            var ka = unchecked(((0x95F44748u * (uint)i + 0x0BBE3F00u) ^ 0x24A3038Fu) + 0x42C9B098u) ^ 0x76878AF8u;
            var n = startup.AsSpan(Slot(0x6C) + i * 44, 44);
            var k = unchecked(0x1E70F997u * (uint)((0x42DCDAFE4FA8UL * (uint)i) >> 20) + 0x50F12A02u);
            var name = new Il2CppAssemblyNameDefinition
            {
                NameIndex = unchecked((int)((U32(n, 0x1C) + 0xFE3F0D29u) ^ k)),
                CultureIndex = unchecked((int)((U32(n, 0) + 0xE291EE7Bu) ^ k)),
                PublicKeyIndex = -1,
                HashValueIndex = -1,
                HashAlg = unchecked((AssemblyHashAlgorithm)((U32(n, 8) + 0xFA4F66EDu) ^ k)),
                HashLen = unchecked((int)(U32(n, 0xC) ^ k ^ 0x07C79475u)),
                Flags = unchecked((AssemblyNameFlags)(U32(n, 0x18) ^ k ^ 0x0F92DC35u)),
                Major = unchecked((int)((U32(n, 0x10) + 0x942E5695u) ^ k)),
                Minor = unchecked((int)(U32(n, 4) ^ k ^ 0x31A1995Eu)),
                Build = unchecked((int)((U32(n, 0x14) + 0x97409857u) ^ k)),
                Revision = unchecked((int)((U32(n, 0x20) + 0xFB971711u) ^ k)),
            };
            for (var b = 0; b < 8; b++)
                name.PublicKeyToken[b] = n[0x24 + b];
            return new()
            {
                ImageIndex = unchecked((int)((U32(d, 8) ^ 0x0CDB5378u) - ka)),
                Token = unchecked((U32(d, 0xC) ^ 0x17DA58F7u) - ka),
                ReferencedAssemblyStart = unchecked((int)((U32(d, 0) ^ 0x7ADC4072u) - ka)),
                ReferencedAssemblyCount = unchecked((int)(U32(d, 4) - ka - 0x1ABD7383u)),
                Aname = name,
            };
        }

        private static readonly (int Offset, int Kind, uint Key)[] HeaderCiphers =
        [
            (0x00C, 0, 0x002A5FBAu),
            (0x010, 0, 0x0BBFCA3Au),
            (0x014, 1, 0x816CE90Cu),
            (0x018, 1, 0xA1C835BEu),
            (0x01C, 0, 0x49CCBAD1u),
            (0x020, 1, 0xA5D24562u),
            (0x02C, 1, 0xE412B6A9u),
            (0x038, 1, 0x929CF336u),
            (0x03C, 0, 0x6BD889C8u),
            (0x044, 0, 0x32EA0436u),
            (0x04C, 1, 0xE5D249BFu),
            (0x05C, 0, 0x6B2B3963u),
            (0x060, 0, 0x15335474u),
            (0x064, 1, 0xE6130C67u),
            (0x068, 0, 0x146BE503u),
            (0x06C, 1, 0xE028E1F0u),
            (0x070, 2, 0x6EA5B3FDu),
            (0x074, 0, 0x245177E0u),
            (0x078, 0, 0x70C36CACu),
            (0x080, 0, 0x0A989FACu),
            (0x084, 0, 0x582F3633u),
            (0x088, 1, 0xF3C0B2FEu),
            (0x08C, 1, 0xD7BBE415u),
            (0x094, 1, 0xF15F5122u),
            (0x098, 0, 0x76A06155u),
            (0x09C, 1, 0xE6D5928Cu),
            (0x0A0, 1, 0xE2AC71C1u),
            (0x0A4, 0, 0x1496B860u),
            (0x0AC, 0, 0x6F3B0AC0u),
            (0x0C8, 0, 0x08D701A3u),
            (0x0D0, 1, 0x901B547Du),
            (0x0D4, 1, 0xE292EFE6u),
            (0x0DC, 0, 0x5278CA3Bu),
            (0x0E8, 1, 0xB4EE6C01u),
            (0x0EC, 1, 0xD7D662B1u),
            (0x110, 0, 0x1AB6109Au),
            (0x118, 0, 0x4009A6BFu),
            (0x120, 0, 0x1C3F68EAu),
            (0x124, 1, 0xC45F877Bu),
            (0x128, 0, 0x371354A2u),
            (0x12C, 0, 0x24898706u),
            (0x134, 1, 0x8EEFDB56u),
            (0x13C, 0, 0x28B1D785u),
            (0x144, 1, 0xD1F800F2u),
            (0x14C, 0, 0x2F249C2Fu),
            (0x150, 1, 0xC8542DD2u),
            (0x158, 1, 0xE2ABA334u),
            (0x15C, 0, 0x00A282EBu),
            (0x160, 1, 0xB57FD84Fu),
            (0x164, 0, 0x03893506u),
            (0x170, 4, 0x028D714Cu),
            (0x174, 1, 0xCED1EDB1u),
            (0x178, 1, 0xDFFBC3AEu),
            (0x188, 0, 0x57DD5CA8u),
            (0x18C, 0, 0x0A6DD06Fu),
            (0x194, 0, 0x35506CF3u),
            (0x198, 0, 0x5303F399u),
            (0x19C, 0, 0x159FBC84u),
            (0x1A0, 1, 0x80130D0Fu),
            (0x1A4, 0, 0x0D8C5D0Au),
            (0x1A8, 1, 0xA8B3BA59u),
            (0x1B0, 0, 0x7F990D7Fu),
            (0x1B8, 0, 0x586ABAF1u),
            (0x1D0, 0, 0x07D0BBF9u),
            (0x1DC, 0, 0x05AC819Bu),
            (0x1E0, 0, 0x2EE480FBu),
            (0x1E4, 0, 0x67102427u),
            (0x1E8, 3, 0x074314ECu),
            (0x1F4, 0, 0x7A3B781Eu),
            (0x200, 1, 0xB43E0EB1u),
        ];
    }
}
