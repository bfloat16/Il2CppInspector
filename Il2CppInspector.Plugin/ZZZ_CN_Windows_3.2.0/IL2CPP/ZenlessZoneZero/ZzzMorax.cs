using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInspector.Next;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector
{
    /// <summary>Static C# adapter for the IDA-verified ZZZ 3.2.0 Windows MORAX build.</summary>
    internal sealed partial class ZzzMorax : Plugins.GameMetadataAdapter
    {
        public override string StorageBase(uint tag) =>
            tag switch
            {
                0 => "klass->static_fields",
                1 => "thread static storage",
                2 => "MetadataRegistration->globalStaticStorage (+0x60)",
                4 => "*MetadataRegistration->globalStaticStorageSlot (+0x78)",
                _ => "unknown",
            };

        public override int VTableSlotSize => 8;
        private const int Payload = 0x198;
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

        internal static ushort U16(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(o, 2));

        internal static short I16(ReadOnlySpan<byte> d, int o) => unchecked((short)U16(d, o));

        internal static uint U32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(o, 4));

        internal static int I32(ReadOnlySpan<byte> d, int o) => unchecked((int)U32(d, o));

        internal static ulong U64(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt64LittleEndian(d.Slice(o, 8));

        internal static int Index16(ushort v) => v == ushort.MaxValue ? -1 : v;

        private int Slot(int o) => slots[o];

        private int Base(int o) => checked(Payload + Slot(o));

        internal int DefaultBlobOffset => Base(0x0A0);
        private delegate T RecordReader<T>(ReadOnlySpan<byte> d, int index);

        private ImmutableArray<T> Records<T>(byte[] bytes, int offset, int count, int stride, RecordReader<T> read)
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
                throw new InvalidDataException($"ZZZ table outside input: offset 0x{offset:X}, length 0x{length:X}.");
            }
        }

        private ZzzMorax(IFileFormatStream image, byte[] globalData, byte[] startupData, EventHandler<string> status, Plugins.GamePlugin plugin)
            : base(plugin)
        {
            Image = image;
            global = globalData;
            startup = startupData;
            if (image is not PEReader || image.Bits != 64 || image.Arch != "x64")
            {
                throw new NotSupportedException("ZenlessZoneZero currently supports the 3.2.0 Windows x64 build.");
            }

            if (!ZzzMetadataDetector.IsMorax(global))
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
            // +198 is the file payload base, not a proven embedded-header size. Read only verified slots.
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
                        _ => unchecked(((int)raw >> 3) ^ (int)key),
                    }
                );
            }
            // MHY is shared by several games. Reject unknown layouts before allocating table arrays.
            if (Slot(0x180) / 80 != 95439 || Slot(0x0CC) / 30 != 829363 || Slot(0x0F0) / 40 != 170 || (image.ReadMappedUInt32(MetadataRegistrationAddress + 0x1C) ^ 0x56478D25) != 939421)
            {
                throw new NotSupportedException("This MORAX build is not the supported ZenlessZoneZero 3.2.0 Windows layout. Keys vary per build.");
            }

            Metadata = Metadata.CreateForPlugin(global, this, MetadataVersions.V245, status);
            status?.Invoke(this, "Decoding ZenlessZoneZero 3.2.0 metadata in C#");
            ReadMetadata();
        }

        internal static Il2CppInspector Load(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status, Plugins.GamePlugin plugin)
        {
            var adapter = new ZzzMorax(image, global, startup, status, plugin);
            var binary = new Il2CppBinaryX64(image, status);
            adapter.ReadBinary(binary);
            return new Il2CppInspector(binary, adapter.Metadata);
        }

        private static ulong[] DiscoverRegistration(IFileFormatStream image)
        {
            // Five consecutive lea rax,[rip+target]; mov [rip+global],rax pairs.
            // Validate the fifth target as an MHY header; no fixed image base or registration RVA.
            foreach (var section in image.GetSections().Where(s => s.IsExec && !s.IsBSS))
            {
                var bytes = image.ReadBytes(section.ImageStart, section.ImageLength);
                for (var i = 0; i <= bytes.Length - 70; i++)
                {
                    if (bytes[i] != 0x48 || bytes[i + 1] != 0x8D || bytes[i + 2] != 0x05)
                    {
                        continue;
                    }

                    var valid = true;
                    for (var p = 0; p < 5; p++)
                    {
                        var o = i + p * 14;
                        if (bytes[o] != 0x48 || bytes[o + 1] != 0x8D || bytes[o + 2] != 0x05 || bytes[o + 7] != 0x48 || bytes[o + 8] != 0x89 || bytes[o + 9] != 0x05)
                        {
                            valid = false;
                            break;
                        }
                    }
                    if (!valid)
                    {
                        continue;
                    }

                    var start = image.MapFileOffsetToVA(section.ImageStart + (uint)i);
                    var targets = new ulong[6];
                    targets[0] = start;
                    for (var p = 0; p < 5; p++)
                    {
                        targets[p + 1] = unchecked((ulong)((long)start + p * 14 + 7 + I32(bytes, i + p * 14 + 3)));
                    }

                    if (image.TryMapVATR(targets[5], out _) && image.ReadMappedUInt32(targets[5]) == 0x0059484D)
                    {
                        return targets;
                    }
                }
            }
            throw new NotSupportedException("Could not locate the ZZZ MORAX registration block and embedded metadata header.");
        }

        private void ReadMetadata()
        {
            Metadata.Images = Records(startup, Slot(0x040), Slot(0x0F0) / 40, 40, Il2CppImageDefinitionReader.FromZzz);
            var typeCount = 0;
            foreach (var image in Metadata.Images)
            {
                if (image.TypeStart != typeCount)
                {
                    throw new InvalidDataException("ZZZ image type ranges are not contiguous.");
                }

                typeCount = checked(typeCount + (int)image.TypeCount);
            }
            var types = Records(global, Base(0x0E4), Slot(0x180) / 80, 80, (d, _) => Il2CppTypeDefinitionReader.FromZzz(d)).ToArray();
            if (typeCount > types.Length || types.Skip(typeCount).Any(t => t.ByValTypeIndex != -1 || t.ByRefTypeIndex != -1 || t.Token != 0x02000000 || (uint)t.NameIndex >> 24 != 0))
            {
                throw new InvalidDataException("Unexpected ZZZ type records outside the image ranges.");
            }
            // The physical table ends with non-type sentinels. Preserve every real definition's index.
            Array.Resize(ref types, typeCount);
            Metadata.Methods = Records(global, Base(0x06C), Slot(0x0CC) / 30, 30, (d, _) => Il2CppMethodDefinitionReader.FromZzz(d));
            var fieldCount = types.Max(t => t.FieldCount == 0 ? 0 : checked((int)t.FieldIndex + t.FieldCount));
            var propertyCount = types.Max(t => t.PropertyCount == 0 ? 0 : checked((int)t.PropertyIndex + t.PropertyCount));
            var eventCount = types.Max(t => t.EventCount == 0 ? 0 : checked((int)t.EventIndex + t.EventCount));
            Metadata.Fields = Records(global, Base(0x128), fieldCount, 12, Il2CppFieldDefinitionReader.FromZzz);
            Metadata.Properties = Records(global, Base(0x080), propertyCount, 14, Il2CppPropertyDefinitionReader.FromZzz);
            Metadata.Events = Records(global, Base(0x104), eventCount, 18, Il2CppEventDefinitionReader.FromZzz);
            var paramCount = Metadata.Methods.Max(m => m.ParameterCount == 0 ? 0 : checked((int)m.ParameterStart + m.ParameterCount));
            Metadata.Params = Records(global, Base(0x17C), paramCount, 12, Il2CppParameterDefinitionReader.FromZzz);
            var nestedCount = types.Max(t => t.NestedTypeCount == 0 ? 0 : (int)t.NestedTypeIndex + t.NestedTypeCount);
            Metadata.NestedTypeIndices = Records(global, Base(0x098), nestedCount, 4, (d, _) => I32(d, 0));
            var interfaceCount = types.Max(t => t.InterfacesCount == 0 ? 0 : (int)t.InterfacesIndex + t.InterfacesCount);
            Metadata.InterfaceUsageIndices = Records(global, Base(0x068), interfaceCount, 4, (d, _) => new TypeIndex(I32(d, 0)));
            var interfaceOffsetsCount = types.Max(t => t.InterfaceOffsetsCount == 0 ? 0 : (int)t.InterfaceOffsetsStart + t.InterfaceOffsetsCount);
            Metadata.InterfaceOffsets = Records(global, Base(0x074), interfaceOffsetsCount, 6, Il2CppInterfaceOffsetPairReader.FromZzz);
            var vtableCount = types.Where(t => t.VTableIndex >= 0 && t.VTableIndex < 0x08000000).Max(t => t.VTableIndex + t.VTableCount);
            Metadata.VTableMethodIndices = Records(global, Base(0x0EC), vtableCount, 4, (d, _) => U32(d, 0));
            for (var i = 0; i < types.Length; i++)
            {
                var t = types[i];
                for (var n = 0; n < t.NestedTypeCount; n++)
                {
                    var child = Metadata.NestedTypeIndices[t.NestedTypeIndex + n];
                    if ((uint)child >= types.Length)
                    {
                        throw new InvalidDataException("ZZZ nested type index out of range.");
                    }

                    types[child].DeclaringTypeIndex = t.ByValTypeIndex;
                }
                if (t.Bitfield.EnumType)
                {
                    for (var f = 0; f < t.FieldCount; f++)
                    {
                        var field = Metadata.Fields[t.FieldIndex + f];
                        if (DecodeString((uint)field.NameIndex) == "value__")
                        {
                            types[i].ElementTypeIndex = field.TypeIndex;
                        }
                    }
                }
            }
            Metadata.Types = ImmutableCollectionsMarshal.AsImmutableArray(types);
            Metadata.Assemblies = Records(startup, Slot(0x0DC), Slot(0x184) / 16, 16, ReadAssembly);
            var containers = types.Select(t => (int)t.GenericContainerIndex).Concat(Metadata.Methods.Select(m => (int)m.GenericContainerIndex)).Max() + 1;
            Metadata.GenericContainers = Records(global, Base(0x05C), containers, 16, Il2CppGenericContainerReader.FromZzz);
            var parameters = Metadata.GenericContainers.Max(c => (int)c.GenericParameterStart + c.TypeArgc);
            Metadata.GenericParameters = Records(global, Base(0x0A8), parameters, 14, (d, _) => Il2CppGenericParameterReader.FromZzz(d));
            var constraints = Metadata.GenericParameters.Max(p => p.ConstraintsCount == 0 ? 0 : p.ConstraintsStart + p.ConstraintsCount);
            Metadata.GenericConstraintIndices = Records(global, Base(0x094), constraints, 4, (d, _) => new TypeIndex(I32(d, 0)));
            Metadata.FieldDefaultValues = Records(global, Base(0x110), Slot(0x0AC) / 12, 12, (d, _) => Il2CppFieldDefaultValueReader.FromZzz(d));
            // 0x1802924BF computes the end with unsigned size / 12; the +1 remainder is not another record.
            Metadata.ParameterDefaultValues = Records(global, Base(0x150), Slot(0x16C) / 12, 12, (d, _) => Il2CppParameterDefaultValueReader.FromZzz(d));
            var attributes = checked((int)Metadata.Images.Max(i => (uint)i.CustomAttributeStart + i.CustomAttributeCount));
            Metadata.AttributeTypeRanges = Records(global, Base(0x07C), attributes, 8, (d, _) => Il2CppCustomAttributeTypeRangeReader.FromZzz(d));
            var attributeTypes = Metadata.AttributeTypeRanges.Max(a => a.Start + a.Count);
            Metadata.AttributeTypeIndices = Records(global, Base(0x0B8), attributeTypes, 4, (d, _) => I32(d, 0));
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

            foreach (var p in Metadata.GenericParameters)
            {
                Intern(p.NameIndex);
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
            {
                return "";
            }

            var offset = (int)(index & 0xFFFFFF);
            var key = unchecked(0x4DFDA3176C3DBCCCUL * (ulong)offset + 0x2CF2420B13CE22DEUL);
            return DecodeBlocks(Base(0x120) + offset, (int)(index >> 24), key, 0x0CE133C72FD1D12FUL);
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
            var count = checked((int)unchecked(Image.ReadMappedUInt32(UsageAddress + 0x3C) + 0xF00152E4u));
            var offsets = Base(0x134);
            uint Offset(int i) => unchecked((uint)(((ulong)i * 0x58A12820A50UL ^ 0x636D73B4UL) * 0x7F312431UL >> 11)) ^ U32(global, offsets + i * 4) ^ 0x47101BE1;
            CheckRange(global, offsets, checked((count + 1) * 4));
            var literals = new string[count];
            for (var i = 0; i < count; i++)
            {
                var start = checked((int)Offset(i));
                var length = checked((int)Offset(i + 1) - start);
                if (length < 0)
                {
                    throw new InvalidDataException("ZZZ literal offsets are not monotonic.");
                }

                var key = unchecked(0xC2BB8E95236B48D8UL * (ulong)i + 0x763311C65C829658UL) ^ 0x3CD34043046E11E1UL;
                literals[i] = DecodeBlocks(Base(0x058) + start, length, key, 0x26210AE2666D5856UL);
            }
            Metadata.StringLiterals = literals;
        }

        private Il2CppAssemblyDefinition ReadAssembly(ReadOnlySpan<byte> d, int i)
        {
            var k = unchecked(((uint)i * 0x4821A9A2u ^ 0x4ED972D6u) * 0x38874DB5u);
            var nameData = startup.AsSpan(Slot(0x0B4) + i * 44, 44);
            var nk = unchecked((uint)i * 0x7E72DF60u + 0x66AFC7ABu);
            var name = new Il2CppAssemblyNameDefinition
            {
                NameIndex = unchecked((int)(U32(nameData, 0x14) ^ nk ^ 0x65382A76u)),
                CultureIndex = unchecked((int)(U32(nameData, 0x18) ^ nk ^ 0x79E1E7A3u)),
                // Runtime name -> MonoAssemblyName (0x18026C489) decodes +10 as hash_len.
                // The same adapter explicitly sets the full public-key pointer to null (0x18026C46E).
                PublicKeyIndex = -1,
                HashAlg = (AssemblyHashAlgorithm)(U32(nameData, 4) ^ nk ^ 0x1F5285D6u),
                HashLen = unchecked((int)(nk ^ (U32(nameData, 0x10) - 0x610A04DEu) ^ 0x1316E58Eu ^ 0x794417C5u)),
                // Remove the initializer's runtime field encoding, then match the assembly-name formatter.
                // 0x18026F218 / 0x18026C6E3; minor/build/revision at 0x18026C716/739/779.
                Major = unchecked((int)((U32(nameData, 0x20) - 0x4883D98Au) ^ nk ^ 0x6A52F24Bu)),
                Minor = unchecked((int)((U32(nameData, 0x08) - 0x3C5D4FDEu) ^ nk ^ 0x3336362Eu ^ 0x5964C465u)),
                Build = unchecked((int)((U32(nameData, 0x0C) - 0x0568CFD0u) ^ nk ^ 0x0DB32786u ^ 0x67E1D5CDu)),
                Revision = unchecked((int)(U32(nameData, 0x00) ^ nk ^ 0x1547E811u ^ 0x5284A9E8u)),
                Flags = (AssemblyNameFlags)unchecked((U32(nameData, 0x1C) - 0x17E42DE5u) ^ nk ^ 0x6A52F24Bu),
            };
            nameData.Slice(0x24, 8).CopyTo(name.PublicKeyToken);
            return new()
            {
                Aname = name,
                ImageIndex = unchecked((int)((U32(d, 8) + 0x8D59E847u) ^ k)),
                Token = U32(d, 4) ^ k ^ 0x52C2CD16u,
                ReferencedAssemblyStart = unchecked((int)((U32(d, 0) + 0xB3CBFD10u) ^ k)),
                ReferencedAssemblyCount = unchecked((int)(U32(d, 12) ^ k ^ 0x35476E05u ^ 0x1E0383E1u)),
            };
        }

        internal bool IsCode(ulong va) => va != 0 && executableSections.Any(s => va >= s.VirtualStart && va <= s.VirtualEnd);

        // 47 verified slots, including the six-byte interface-offset table (+74).
        private static readonly (int Offset, int Kind, uint Key)[] HeaderCiphers =
        [
            (0x014, 1, 0xF684DF6A),
            (0x018, 1, 0xB89340E0),
            (0x028, 1, 0xC7CE0A8B),
            (0x040, 0, 0x438F3FC2),
            (0x048, 0, 0x7599DC99),
            (0x050, 0, 0x3AF46F99),
            (0x058, 0, 0x43CA0124),
            (0x05C, 1, 0xB1D82EC3),
            (0x068, 2, 0x23082117),
            (0x06C, 1, 0xCCEB3F69),
            (0x074, 2, 0x3F7EED5D),
            (0x07C, 1, 0xA2A1E811),
            (0x080, 1, 0xA2039705),
            (0x08C, 0, 0x0B9F9FF3),
            (0x094, 0, 0x01EACF73),
            (0x098, 2, 0x35A104DB),
            (0x0A0, 2, 0x3838321D),
            (0x0A8, 0, 0x6E38E07F),
            (0x0AC, 0, 0x3E9D1CB8),
            (0x0B4, 0, 0x7339297D),
            (0x0B8, 0, 0x1982EE8D),
            (0x0BC, 1, 0xE7A4DC1B),
            (0x0C0, 0, 0x75E0BAD8),
            (0x0CC, 0, 0x5B532022),
            (0x0DC, 1, 0x93E32413),
            (0x0E4, 1, 0xA1AF1871),
            (0x0EC, 1, 0xEA61CDD9),
            (0x0F0, 1, 0xC4E0F21B),
            (0x104, 1, 0xB91A804E),
            (0x108, 3, 0x0B5D4714),
            (0x110, 1, 0xB9FF98B0),
            (0x118, 1, 0xDDB2CDD3),
            (0x120, 0, 0x44A002F4),
            (0x128, 1, 0x936515E2),
            (0x134, 1, 0x9F0D227C),
            (0x140, 1, 0xE82C0132),
            (0x144, 0, 0x37D1D2B7),
            (0x150, 0, 0x1367C9C4),
            (0x15C, 1, 0xA5C9835B),
            (0x16C, 0, 0x32AE8BC8),
            (0x174, 1, 0x81B57508),
            (0x178, 1, 0xD9F04069),
            (0x17C, 0, 0x5A988D21),
            (0x180, 0, 0x560BB290),
            (0x184, 1, 0xEBA08FFA),
            (0x188, 1, 0x990AE8F1),
            (0x190, 0, 0x023A0C2B),
        ];
    }
}
