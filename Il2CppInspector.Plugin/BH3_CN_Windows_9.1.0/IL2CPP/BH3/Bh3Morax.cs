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
    /// <summary>Static C# adapter for the IDA-verified Honkai Impact 3rd 9.1.0 Windows MORAX build.</summary>
    internal sealed partial class Bh3Morax : Plugins.GameMetadataAdapter
    {
        public override string StorageBase(uint tag) =>
            tag switch
            {
                0 => "klass->static_fields (+0x30)",
                1 => "thread static storage",
                2 => "MetadataRegistration->globalStaticStorage (+0x90)",
                4 => "*qword_182555530 (global static storage B)",
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

        // B §4: this build stores no metadata version (hdr+0x04 is zero and is never read by the
        // binary). The value below only feeds the stock pipeline's version gates; every table is
        // decoded explicitly by this adapter.
        private static readonly StructVersion NominalMetadataVersion = MetadataVersions.V245;

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
                throw new InvalidDataException("BH3 table boundaries do not match the record stride.");
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
                throw new InvalidDataException($"BH3 table outside input: offset 0x{offset:X}, length 0x{length:X}.");
            }
        }

        private Bh3Morax(IFileFormatStream image, byte[] globalData, byte[] startupData, EventHandler<string> status, Plugins.GamePlugin plugin)
            : base(plugin)
        {
            Image = image;
            global = globalData;
            startup = startupData;
            if (image is not PEReader || image.Bits != 64 || image.Arch != "x64")
            {
                throw new NotSupportedException("Honkai Impact 3rd currently supports the 9.1.0 Windows x64 build.");
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
            // The effective header is the 0x198-byte "MHY\0" block embedded in UserAssembly.dll,
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
                        _ => unchecked((int)((raw >> 3) ^ key)),
                    }
                );
            }
            // MHY is shared by several games. Reject unknown layouts before allocating table arrays.
            ValidateLayout();

            Metadata = Metadata.CreateForPlugin(global, this, NominalMetadataVersion, status);
            status?.Invoke(this, "Decoding Honkai Impact 3rd 9.1.0 metadata in C#");
            ReadMetadata();
        }

        internal static Il2CppInspector Load(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status, Plugins.GamePlugin plugin)
        {
            var adapter = new Bh3Morax(image, global, startup, status, plugin);
            var binary = new Il2CppBinaryX64(image, status);
            adapter.ReadBinary(binary);
            return new Il2CppInspector(binary, adapter.Metadata);
        }

        // Payload-relative table offsets verified against the reports. Any mismatch means this is a
        // different MORAX build whose slot keys and table layout do not apply.
        private void ValidateLayout()
        {
            if (
                Slot(0x01C) != 0x3E319F8
                || Slot(0x0D4) != 0x2E141A0
                || Slot(0x128) != 0x13395D0
                || Slot(0x044) != 0x2064630
                || Slot(0x190) != 0xFB904
                || Slot(0x060) != 0x291AC3C
                || Slot(0x164) != 0x1EB360
                || Slot(0x064) != 0x2ADA398
                || Slot(0x028) != 0x25E721C
                || Slot(0x098) / 82 != 77801
                || TableCount(0x0D4, 0x0F0, 30) != 561798
                || TableCount(0x07C, 0x11C, 12) != 61545
            )
            {
                throw new NotSupportedException("This MORAX build is not the supported Honkai Impact 3rd 9.1.0 Windows layout. Keys vary per build.");
            }
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
            throw new NotSupportedException("Could not locate the BH3 MORAX registration block and embedded metadata header.");
        }

        private void ReadMetadata()
        {
            Metadata.Images = Records(startup, Slot(0x0A4), TableCount(0x0A4, 0x02C, 40), 40, Il2CppImageDefinitionReader.FromBh3);
            // G §1 / §3: typeStart and typeCount live at +0x08 / +0x10 and the 212 image ranges
            // cover the whole 77,799-entry type table without a gap.
            var typeCount = 0;
            foreach (var image in Metadata.Images)
            {
                if (image.TypeStart != typeCount)
                {
                    throw new InvalidDataException("BH3 image type ranges are not contiguous.");
                }

                typeCount = checked(typeCount + (int)image.TypeCount);
            }
            // Image ranges exclude the two physical sentinels; read only real definitions.
            var types = Records(global, Base(0x01C), typeCount, 82, (d, _) => Il2CppTypeDefinitionReader.FromBh3(d));
            Metadata.Types = types;
            Metadata.Methods = Records(global, Base(0x0D4), TableCount(0x0D4, 0x0F0, 30), 30, Il2CppMethodDefinitionReader.FromBh3);
            var fieldCount = types.Max(t => t.FieldCount == 0 ? 0 : checked((int)t.FieldIndex + t.FieldCount));
            var propertyCount = types.Max(t => t.PropertyCount == 0 ? 0 : checked((int)t.PropertyIndex + t.PropertyCount));
            var eventCount = types.Max(t => t.EventCount == 0 ? 0 : checked((int)t.EventIndex + t.EventCount));
            Metadata.Fields = Records(global, Base(0x128), fieldCount, 12, Il2CppFieldDefinitionReader.FromBh3);
            Metadata.Properties = Records(global, Base(0x190), propertyCount, 14, Il2CppPropertyDefinitionReader.FromBh3);
            Metadata.Events = Records(global, Base(0x10C), eventCount, 18, Il2CppEventDefinitionReader.FromBh3);
            var paramCount = Metadata.Methods.Max(m => m.ParameterCount == 0 ? 0 : checked((int)m.ParameterStart + m.ParameterCount));
            Metadata.Params = Records(global, Base(0x044), paramCount, 12, Il2CppParameterDefinitionReader.FromBh3);
            var interfaceCount = types.Max(t => t.InterfacesCount == 0 ? 0 : (int)t.InterfacesIndex + t.InterfacesCount);
            Metadata.InterfaceUsageIndices = Records(global, Base(0x178), interfaceCount, 4, (d, _) => new TypeIndex(I32(d, 0)));
            var interfaceOffsetsCount = types.Max(t => t.InterfaceOffsetsCount == 0 ? 0 : (int)t.InterfaceOffsetsStart + t.InterfaceOffsetsCount);
            Metadata.InterfaceOffsets = Records(global, Base(0x074), interfaceOffsetsCount, 6, (d, _) => Il2CppInterfaceOffsetPairReader.FromBh3(d));
            // G §2.3: nestedTypesStart indexes the 4-byte table at hdr[0x0CC]; the 27,253 entries
            // exactly cover max(nestedTypesStart + nestedTypeCount).
            var nestedCount = types.Max(t => t.NestedTypeCount == 0 ? 0 : (int)t.NestedTypeIndex + t.NestedTypeCount);
            Metadata.NestedTypeIndices = Records(global, Base(0x0CC), nestedCount, 4, (d, _) => I32(d, 0));
            // A.10: the 4-byte vtable method index table is at hdr[0x030]; its 1,047,605 entries end
            // exactly at the interface-offset table.
            var vtableCount = types.Where(t => t.VTableIndex >= 0).Max(t => t.VTableIndex + t.VTableCount);
            // Entries use the ordinary kind/index encoding; kinds 3 and 6 select methods.
            Metadata.VTableMethodIndices = Records(global, Base(0x030), vtableCount, 4, (d, _) => U32(d, 0));
            Metadata.Assemblies = Records(startup, Slot(0x168), Metadata.Images.Length, 16, ReadAssembly);
            Metadata.GenericContainers = Records(global, Base(0x094), TableCount(0x094, 0x190, 16), 16, Il2CppGenericContainerReader.FromBh3);
            var parameters = Metadata.GenericContainers.Max(c => (int)c.GenericParameterStart + c.TypeArgc);
            Metadata.GenericParameters = Records(global, Base(0x060), parameters, 14, Il2CppGenericParameterReader.FromBh3);
            // G §5: the constraint index table is the 4-byte table at hdr[0x11C]; its 900 entries
            // cover max(constraintsStart + constraintsCount).
            var constraints = Metadata.GenericParameters.Max(p => p.ConstraintsCount == 0 ? 0 : p.ConstraintsStart + p.ConstraintsCount);
            Metadata.GenericConstraintIndices = Records(global, Base(0x11C), constraints, 4, (d, _) => new TypeIndex(I32(d, 0)));
            Metadata.FieldDefaultValues = Records(global, Base(0x07C), TableCount(0x07C, 0x11C, 12), 12, (d, _) => Il2CppFieldDefaultValueReader.FromBh3(d));
            Metadata.ParameterDefaultValues = Records(global, Base(0x138), TableCount(0x138, 0x0D0, 12), 12, (d, _) => Il2CppParameterDefaultValueReader.FromBh3(d));
            var attributeCount = 0;
            foreach (var image in Metadata.Images)
            {
                if (image.CustomAttributeStart != attributeCount)
                    throw new InvalidDataException("BH3 image attribute ranges are not contiguous.");
                attributeCount = checked(attributeCount + (int)image.CustomAttributeCount);
            }
            // L §2 / §3: packed ranges and plaintext Il2CppType reference indices.
            Metadata.AttributeTypeRanges = Records(
                global,
                Base(0x03C),
                attributeCount,
                8,
                (d, _) =>
                    new Il2CppCustomAttributeTypeRange
                    {
                        Start = (int)(U32(d, 0) & 0xFFFFFF),
                        Count = (int)(U32(d, 0) >> 24),
                        Token = U32(d, 4),
                    }
            );
            Metadata.AttributeTypeIndices = Records(global, Base(0x12C), TableCount(0x12C, 0x070, 4), 4, (d, _) => I32(d, 0));
            Metadata.AttributeDataRanges = [];
            Metadata.MetadataUsageLists = [];
            Metadata.MetadataUsagePairs = [];
            Metadata.TypeInlineArrays = [];
            // The binary pass decodes FieldRefs from hdr[0x050] after reading the usage pairs.
            Metadata.FieldRefs = [];
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
            var seed = unchecked(0x34A3D32F255870B0UL * (ulong)offset - 0x3AE20E7B049152UL) ^ 0x574A806E26B56AD3UL;
            return DecodeBlocks(Base(0x164) + offset, (int)(index >> 24), seed, 0x735E1E977F020CDDUL);
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
            // F §1: the index table ends at the generic function table; its last entry is the blob size
            // sentinel, so the literal count is one less than the table entry count.
            var offsets = Base(0x064);
            var entries = TableCount(0x064, 0x06C, 4);
            var count = checked(entries - 1);
            CheckRange(global, offsets, checked(entries * 4));
            var literals = new string[count];
            for (var i = 0; i < count; i++)
            {
                var start = checked((int)LiteralOffset(offsets, i));
                var length = checked((int)LiteralOffset(offsets, i + 1) - start);
                if (length < 0)
                {
                    throw new InvalidDataException("BH3 literal offsets are not monotonic.");
                }

                literals[i] = DecodeBlocks(Base(0x028) + start, length, LiteralSeed(i), 0x5CC8058D674CBC7AUL);
            }
            Metadata.StringLiterals = literals;
        }

        private uint LiteralOffset(int offsets, int i) => unchecked((U32(global, offsets + i * 4) + 0xB264246Bu) ^ LiteralKs(i));

        private static uint LiteralKs(int i)
        {
            var r = unchecked((ulong)(uint)i * 0xAC8CUL);
            r ^= 0x51FBA0AEUL;
            r = unchecked(r * 0x1EECA12DUL);
            r >>= 0x15;
            return unchecked((uint)r * 0x5558B895u + 0x15FAAF68u);
        }

        private static ulong LiteralSeed(int i)
        {
            var r = unchecked((ulong)(uint)i * 0x3499EE547F1E1685UL);
            r ^= 0x1B2DFB1D563BE541UL;
            r = unchecked(r * 0x40EA14405A88D87BUL);
            r ^= 0x614CCD982223493FUL;
            r = unchecked(r * 0x6E57E69552A5DD26UL);
            r ^= 0x53C674CF69ADE2C6UL;
            return r;
        }

        private Il2CppAssemblyDefinition ReadAssembly(ReadOnlySpan<byte> d, int i)
        {
            // E §1 / E §2: the assembly header and the assembly name are two tables in startup-metadata.dat.
            var k = AssemblyKs(i);
            var nameData = startup.AsSpan(Slot(0x05C) + i * 44, 44);
            var nk = AssemblyNameKs(i);
            var name = new Il2CppAssemblyNameDefinition
            {
                NameIndex = unchecked((int)((U32(nameData, 8) ^ 0x2B03787u) - nk)),
                CultureIndex = unchecked((int)((U32(nameData, 4) ^ 0x4C895721u) - nk)),
                PublicKeyIndex = -1,
                // G §4 recovers the ordinary assembly identity fields from the shuffled name record.
                HashAlg = unchecked((AssemblyHashAlgorithm)((U32(nameData, 0x0C) ^ 0x7A4100CEu) - nk)),
                HashLen = 0,
                Major = unchecked((int)(U32(nameData, 0x00) - nk - 0x58861A7Cu)),
                Minor = unchecked((int)((U32(nameData, 0x18) ^ 0x146F9C83u) - nk)),
                Build = unchecked((int)((U32(nameData, 0x20) ^ 0x65115E0Du) - nk)),
                Revision = unchecked((int)((U32(nameData, 0x1C) ^ 0x172F3C57u) - nk)),
                Flags = unchecked((AssemblyNameFlags)((U32(nameData, 0x10) ^ 0x1F5DA438u) - nk)),
            };
            // +0x24..+0x2B is the plaintext public key token.
            nameData.Slice(0x24, 8).CopyTo(name.PublicKeyToken);
            return new()
            {
                Aname = name,
                ImageIndex = unchecked((int)(U32(d, 8) ^ k ^ 0x6503A2Eu)),
                Token = unchecked((U32(d, 0) + 0x80ABFBD3u) ^ k),
                ReferencedAssemblyStart = unchecked((int)((U32(d, 4) + 0x8C27A915u) ^ k)),
                // TODO: 未确定 — E.1 +0x0C has no confirmed semantics.
                ReferencedAssemblyCount = 0,
            };
        }

        // E §1: assembly header keystream (startup, stride 16).
        private static uint AssemblyKs(int i)
        {
            var x = unchecked(0x1D9287105C50UL * (ulong)(uint)i + 0x58BB516B922650UL) >> 0xA;
            x ^= 0x6FB432AEUL;
            x = unchecked(x * 0x5C97FAD5UL) >> 0xD;
            return unchecked((uint)x);
        }

        // E §2: assembly name keystream (startup, stride 44).
        private static uint AssemblyNameKs(int i)
        {
            var x = unchecked(0xA7FD3669600UL * (ulong)(uint)i + 0x1EB4E4C32714UL) >> 0xD;
            return unchecked((uint)(x ^ 0x4FC59067UL));
        }

        internal bool IsCode(ulong va) => va != 0 && executableSections.Any(s => va >= s.VirtualStart && va <= s.VirtualEnd);

        // Decode only slots consumed by the adapter: 0 = XOR, 1 = ADD,
        // 3 = logical SHR3 then XOR. Unused recovered keys stay in the analysis reports.
        private static readonly (int Offset, int Kind, uint Key)[] HeaderCiphers =
        [
            (0x01C, 0, 0x3AC76B0F),
            (0x028, 0, 0x1060CB63),
            (0x02C, 0, 0x7501F887),
            (0x030, 1, 0xAB04EFBF),
            (0x03C, 1, 0xF62DA3E4),
            (0x044, 1, 0xD3C91887),
            (0x04C, 1, 0xD130496F),
            (0x050, 1, 0xCFD02B21),
            (0x054, 1, 0xBAD8E2FD),
            (0x05C, 1, 0xFBBA1783),
            (0x060, 0, 0x6E00B650),
            (0x064, 1, 0xCE47FD89),
            (0x06C, 1, 0xE4ABBA8D),
            (0x070, 1, 0xFF812B28),
            (0x074, 1, 0xBF38CEAF),
            (0x07C, 1, 0xEF4BBCDC),
            (0x094, 0, 0x5D5A4B4A),
            (0x098, 0, 0x64C3BC60),
            (0x0A4, 1, 0xE513D90B),
            (0x0BC, 0, 0x3EF976E7),
            (0x0CC, 0, 0x7CC78CB4),
            (0x0D0, 0, 0x472B068B),
            (0x0D4, 0, 0x2360832F),
            (0x0EC, 3, 0x075F019B),
            (0x0F0, 0, 0x7FC56EAE),
            (0x100, 1, 0x9D48B319),
            (0x10C, 1, 0xE63914B0),
            (0x110, 1, 0x9918BFB0),
            (0x118, 0, 0x7236E008),
            (0x11C, 0, 0x07080720),
            (0x120, 0, 0x3342BABC),
            (0x128, 1, 0xCA7D051D),
            (0x12C, 0, 0x30DA1022),
            (0x138, 0, 0x67AF3914),
            (0x13C, 1, 0xCD825A2F),
            (0x164, 0, 0x758BB6D9),
            (0x168, 1, 0xD955C02B),
            (0x178, 1, 0xC145F98A),
            (0x190, 0, 0x537836F9),
        ];
    }
}
