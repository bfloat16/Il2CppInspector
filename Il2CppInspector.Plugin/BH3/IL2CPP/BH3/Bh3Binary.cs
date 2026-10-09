using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector
{
    internal sealed partial class Bh3Morax
    {
        public override List<MetadataUsage> Usages { get; } = [];
        private List<(int TypeIndex, ulong Address)> RuntimeCaches { get; } = [];
        public override IEnumerable<Plugins.AdditionalMetadataUsage> AdditionalUsages =>
            RuntimeCaches.Select(cache => new Plugins.AdditionalMetadataUsage("bh3RuntimeCaches", cache.TypeIndex, cache.Address, $"RuntimeCache_{cache.TypeIndex}_{cache.Address:X}", "void *"));
        private uint[] rawFieldOffsets;
        internal Dictionary<int, int> GenericLayoutGroups { get; } = [];

        // D §3.3: the generic class table lives in startup-metadata.dat at Slot(0x02C). Each 8-byte
        // record is {u32 typeDefinitionIndex; u32 classInstIndex}, with classInstIndex == -1 meaning
        // "no instance". The Il2CppType data of a GENERICINST reference is the record ordinal.
        internal (int Definition, int Instance) GenericClass(ulong ordinal)
        {
            if (ordinal >= (uint)Slot(0x0EC))
            {
                throw new InvalidDataException("BH3 generic class ordinal out of range.");
            }

            var offset = checked(Slot(0x02C) + (int)ordinal * 8);
            return (I32(startup, offset), I32(startup, offset + 4));
        }

        // Generic classes belong to the startup table; hdr[0x118] contains generic methods.
        internal int GenericClassCount => Slot(0x0EC);
        private int GenericMethodCount => TableCount(0x118, 0x110, 8);

        private void ReadBinary(Il2CppBinary binary)
        {
            // D §3.1: the Il2CppType array is plaintext in the DLL at MR+0x70. The count is stored at
            // MR+0x40 as raw - 0x15796DC9.
            var typesVa = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x70);
            var typeCount = checked((int)(Image.ReadMappedUInt32(MetadataRegistrationAddress + 0x40) - 0x15796DC9u));
            var typeBytes = Image.ReadMappedBytes(typesVa, checked(typeCount * 16));
            var types = new Il2CppType[typeCount];
            var typeAddresses = new Dictionary<ulong, int>(typeCount);
            for (var i = 0; i < typeCount; i++)
            {
                var o = i * 16;
                var bits = typeBytes[o + 11];
                // Only byref (bit 6) occurs in this build. Do not guess pinned/modifier bits.
                if ((bits & ~0x40) != 0)
                {
                    throw new InvalidDataException("Unverified BH3 Il2CppType flag bits.");
                }

                types[i] = new()
                {
                    Data = new() { Value = U64(typeBytes, o) },
                    Value = U16(typeBytes, o + 8) | (uint)typeBytes[o + 10] << 16 | (uint)(bits & 0x40) << 23,
                };
                typeAddresses.Add(typesVa + (ulong)o, i);
            }

            // D §3.2: Il2CppGenericInst at MR+0x78; count = MR+0x3C ^ 0x4D2509F2.
            var instCount = checked((int)(Image.ReadMappedUInt32(MetadataRegistrationAddress + 0x3C) ^ 0x4D2509F2u));
            var instsVa = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x78);
            var instances = Image.ReadMappedVersionedObjectArray<Il2CppGenericInst>(instsVa, instCount);
            if (instances.Any(inst => inst.TypeArgc is 0 or > 64))
                throw new InvalidDataException("Invalid BH3 generic argument count.");

            // The 8-byte generic method table is normalized to stock MethodSpecs. The separate
            // 12-byte table at hdr[0x06C] supplies function pointers and invoker indices below.
            var specs = Records(global, Base(0x118), GenericMethodCount, 8, (d, _) => ReadMethodSpec(d, instances.Length));

            // D §A.7 / §B.2: each type definition maps to a layout group through hdr[0x120]; the group
            // record at hdr[0xBC] holds the sizes and the field offset start, and the raw field
            // offsets are the u32 table at hdr[0xD0].
            var sizes = new Il2CppTypeDefinitionSizes[Metadata.Types.Length];
            rawFieldOffsets = new uint[Metadata.Fields.Length];
            var normalizedOffsets = new uint[rawFieldOffsets.Length];
            for (var i = 0; i < sizes.Length; i++)
            {
                var type = Metadata.Types[i];
                var group = DefinitionLayoutGroup(i);
                sizes[i] = LayoutSizes(group);
                var offsetIndex = checked((int)U32(global, Base(0x0BC) + group * 12 + 8));
                for (var f = 0; f < type.FieldCount; f++)
                {
                    var fi = type.FieldIndex + f;
                    var raw = U32(global, Base(0x0D0) + (offsetIndex + f) * 4);
                    rawFieldOffsets[fi] = raw;
                    normalizedOffsets[fi] = DecodeFieldOffset(raw);
                }
            }

            // D §4.1 / §A.7: the 28,010-entry shared generic table at startup + hdr[0x100] holds
            // Il2CppType indices of constructed generics; hdr[0x054] holds their layout groups.
            var layoutTypes = Slot(0x100);
            var layoutGroups = Slot(0x054);
            for (var i = 0; i < TableCount(0x100, 0x0A4, 4); i++)
            {
                var type = types[U32(startup, layoutTypes + i * 4)];
                if (type.Type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
                {
                    GenericLayoutGroups.TryAdd(checked((int)type.Data.Value), checked((int)U32(startup, layoutGroups + i * 4)));
                }
            }

            binary.InitializeBh3(
                this,
                ImmutableCollectionsMarshal.AsImmutableArray(types),
                typeAddresses,
                instances,
                specs,
                ImmutableCollectionsMarshal.AsImmutableArray(normalizedOffsets),
                ImmutableCollectionsMarshal.AsImmutableArray(sizes)
            );

            // D §2.1: the Il2CppCodeGenModule pointer array is at CR+0x28 with its count at CR+0x30.
            var moduleCount = Image.ReadMappedUInt32(CodeRegistrationAddress + 0x30) ^ 0x4ED23D5Eu;
            if (moduleCount != Metadata.Images.Length)
            {
                throw new InvalidDataException("BH3 module/image count mismatch.");
            }

            var modulesVa = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x28);
            for (var i = 0; i < moduleCount; i++)
            {
                var image = Metadata.Images[i];
                // D §2.2: method pointers are addressed by the low 24 bits of a method token, so the
                // per-module arrays span the largest token row id of the module's types.
                var count = 0;
                for (var t = (int)image.TypeStart; t < image.TypeStart + image.TypeCount; t++)
                {
                    var td = Metadata.Types[t];
                    for (var m = (int)td.MethodIndex; m < td.MethodIndex + td.MethodCount; m++)
                    {
                        count = Math.Max(count, checked((int)(Metadata.Methods[m].Token & 0xFFFFFF)));
                    }
                }

                var moduleVa = Image.ReadMappedUInt64(modulesVa + (ulong)i * 8);
                // D §2.2: +0x28 is the module name, +0x3C the tuple count, +0x48 the
                // {u32 token, u64 pointer} tuple array and +0x5C the module index.
                var name = Metadata.Strings[image.NameIndex];
                var moduleName = Image.ReadMappedUInt64(moduleVa + 0x28);
                if (Image.ReadMappedNullTerminatedString(moduleName) != name)
                {
                    throw new InvalidDataException("BH3 code generation module name mismatch.");
                }

                var decodedCount = Image.ReadMappedUInt32(moduleVa + 0x3C) ^ 0x763AC9A5u;
                var tupleVa = Image.ReadMappedUInt64(moduleVa + 0x48);
                var pointersVa = Image.ReadMappedUInt64(moduleVa + 0x50);
                var pointers = count == 0 ? [] : Image.ReadMappedUWordArray(pointersVa, count);
                for (var m = 0; m < pointers.Length; m++)
                    if (!IsCode(pointers[m]))
                        pointers[m] = 0;
                if (tupleVa != 0 && Image.TryMapVATR(tupleVa, out _))
                {
                    for (var e = 0u; e < decodedCount; e++)
                    {
                        // sub_180616E00 uses this token-keyed table for value-type overrides.
                        var row = Image.ReadMappedUInt32(tupleVa + e * 16UL) & 0xFFFFFF;
                        if (row == 0 || row > (uint)pointers.Length)
                        {
                            continue;
                        }

                        var pointer = Image.ReadMappedUInt64(tupleVa + e * 16UL + 8);
                        if (IsCode(pointer))
                        {
                            pointers[row - 1] = pointer;
                        }
                    }
                }
                else if (decodedCount != 0)
                {
                    throw new InvalidDataException("BH3 module method pointer table is missing.");
                }

                // The stored module index is XOR encoded; module order need not equal the index.
                var moduleIndex = Image.ReadMappedUInt32(moduleVa + 0x5C) ^ 0x23A44D5Fu;
                if (moduleIndex >= moduleCount)
                {
                    throw new InvalidDataException("BH3 module invoker index out of range.");
                }

                var module = new Il2CppCodeGenModule
                {
                    ModuleName = moduleName,
                    MethodPointerCount = (uint)pointers.Length,
                    MethodPointers = pointersVa,
                };
                // sub_180616E00 reads u16 invoker indices from image+0x50, whose encoded
                // base is initialized from hdr[0x110] and the module offset table hdr[0x04C].
                var invokersOffset = checked(Base(0x110) + (int)(U32(global, Base(0x04C) + (int)moduleIndex * 4) & ~1u));
                CheckRange(global, invokersOffset, checked(count * 2));
                var invokers = new int[count];
                for (var m = 0; m < count; m++)
                {
                    invokers[m] = Index16(U16(global, invokersOffset + m * 2));
                    if (invokers[m] >= binary.MethodInvokePointers.Length)
                        throw new InvalidDataException("BH3 method invoker outside the registration table.");
                }
                binary.Modules.Add(name, module);
                binary.CodeGenModulePointers.Add(name, moduleVa);
                binary.ModuleMethodPointers.Add(module, pointers);
                binary.MethodInvokerIndices.Add(module, ImmutableCollectionsMarshal.AsImmutableArray(invokers));
            }

            // The 12-byte function table indexes the 8-byte generic method definitions,
            // CR+0x08 method bodies, CR+0x80 adjustors and CR+0x50 invokers (sub_180627060).
            var genericVa = binary.CodeRegistration.GenericMethodPointers;
            var genericCount = binary.CodeRegistration.GenericMethodPointersCount;
            var functionsCount = TableCount(0x06C, 0x0D4, 12);
            for (var i = 0; i < functionsCount; i++)
            {
                var o = Base(0x06C) + i * 12;
                var ordinal = U32(global, o);
                var pointerIndex = U32(global, o + 4);
                var invoker = Index16(U16(global, o + 10));
                if (ordinal >= specs.Length || pointerIndex >= genericCount || invoker >= binary.MethodInvokePointers.Length)
                    throw new InvalidDataException("BH3 generic function index outside the registration tables.");
                var spec = specs[(int)ordinal];
                var pointer = Image.ReadMappedUInt64(genericVa + pointerIndex * 8UL);
                if (IsCode(pointer))
                    binary.GenericMethodPointers.TryAdd(spec, pointer);
                binary.GenericMethodInvokerIndices.TryAdd(spec, invoker);
            }

            ReadUsages(typeCount);
        }

        // sub_180686A60 resolves +0 through sub_18061F410 (a method definition accessor).
        // +6 is the class instantiation; +4 is the method instantiation. All 251,755 records
        // agree with the generic arity of their method and declaring type.
        private Il2CppMethodSpec ReadMethodSpec(ReadOnlySpan<byte> d, int instanceCount)
        {
            var method = I32(d, 0);
            var classInst = Index16(U16(d, 6));
            var methodInst = Index16(U16(d, 4));
            if ((uint)method >= (uint)Metadata.Methods.Length || classInst >= instanceCount || methodInst >= instanceCount)
                throw new InvalidDataException("BH3 generic method definition outside decoded tables.");
            return new()
            {
                MethodDefinitionIndex = method,
                ClassIndexIndex = classInst,
                MethodIndexIndex = methodInst,
            };
        }

        // D §B.2: there is no standalone Il2CppTypeDefinitionSizes in this build. The 12-byte layout
        // group record at global + (hdr[0xBC] ^ 0x3EF976E7) holds {u16 staticFieldsSize;
        // u16 instanceSize; u8 ?; u8 alignment; u8 ?; u32 fieldOffsetStart}.
        public override Il2CppTypeDefinitionSizes LayoutSizes(int group)
        {
            var o = checked(Base(0x0BC) + group * 12);
            return new()
            {
                StaticFieldsSize = U16(global, o),
                InstanceSize = U16(global, o + 2),
                // No marshal size table is located for this build.
                NativeSize = -1,
                ThreadStaticFieldsSize = 0,
            };
        }

        // D §B.2: the instance alignment is the plain byte at +6 (the runtime takes its tzcnt).
        internal int LayoutAlignment(int group) => global[checked(Base(0x0BC) + group * 12 + 6)];

        internal uint LayoutRawFieldOffset(int group, int local)
        {
            var start = checked((int)U32(global, Base(0x0BC) + group * 12 + 8));
            return U32(global, Base(0x0D0) + (start + local) * 4);
        }

        public override uint LayoutFieldOffset(int group, int local) => DecodeFieldOffset(LayoutRawFieldOffset(group, local));

        // D §B.1.3: tag 0 is klass->static_fields, odd tags are thread-static storage, tag 2 is the
        // MetadataRegistration->globalStaticStorage region and tag 4 the *qword_182555530 region.
        // Same partition as the ZZZ build, with different storage addresses.
        private static uint DecodeFieldOffset(uint raw) =>
            (raw >> 24) switch
            {
                1 => (raw & 0xFFFFFF) | 0x80000000,
                2 or 4 => raw & 0xFFFFFF,
                _ => raw,
            };

        // The map ends exactly at Fields: (Base(0x128) - Base(0x120)) / 4 == 77,799.
        internal int DefinitionLayoutGroup(int definition)
        {
            if ((uint)definition >= (uint)Metadata.Types.Length)
                throw new InvalidDataException("BH3 type definition outside the layout map.");
            return checked((int)U32(global, Base(0x120) + definition * 4));
        }

        private void ReadUsages(int typeCount)
        {
            // sub_1806259E0 uses 64-bit intermediates. UsageRegistration holds the linked
            // destination slot arrays, including slots for the lazily allocated kind 7 caches.
            var count = TableCount(0x13C, 0x120, 8);
            var usages = new MetadataUsage[count];
            var bases = new ulong[8];
            bases[1] = Image.ReadMappedUInt64(UsageAddress + 0x30);
            bases[3] = bases[6] = Image.ReadMappedUInt64(UsageAddress + 0x58);
            bases[4] = Image.ReadMappedUInt64(UsageAddress + 0x48);
            bases[5] = Image.ReadMappedUInt64(UsageAddress + 0x18);
            bases[7] = Image.ReadMappedUInt64(UsageAddress + 0x28);
            var caches = new Dictionary<ulong, int>();
            var fieldRefs = TableCount(0x050, 0x0CC, 8);
            var limits = new[] { 0, typeCount, 0, Metadata.Methods.Length, fieldRefs, Metadata.StringLiterals.Length, GenericMethodCount, typeCount };
            for (var i = 0; i < count; i++)
            {
                var x = unchecked((ulong)(uint)i * 0x904EUL + 0x6D79BABDUL) ^ 0x1EE95B5CUL;
                x = unchecked(x * 0x50211227UL) >> 9;
                var key = unchecked((uint)(x * 0x52B87494UL)) ^ 0x783CB04Au;
                var o = Base(0x13C) + i * 8;
                var destination = unchecked(U32(global, o) - 0x0E92425Au) ^ key;
                var source = unchecked(U32(global, o + 4) - 0x4D11429Bu) ^ key;
                var kind = source >> 29;
                var index = checked((int)(source & 0x1FFFFFFF));
                if (kind is not (1 or 3 or 4 or 5 or 6 or 7))
                    throw new InvalidDataException("Unexpected BH3 metadata usage kind.");
                if (index >= limits[kind])
                    throw new InvalidDataException("BH3 usage source outside decoded tables.");
                var address = checked(bases[kind] + destination * 8UL);
                if (kind == 7)
                {
                    if (caches.TryGetValue(address, out var previous) && previous != index)
                        throw new InvalidDataException("Conflicting BH3 runtime cache slots.");
                    caches.TryAdd(address, index);
                    continue;
                }
                usages[i] = new((MetadataUsageType)kind, index, address);
            }
            Metadata.FieldRefs = Records(
                global,
                Base(0x050),
                fieldRefs,
                8,
                (d, i) =>
                {
                    var key = unchecked((uint)i * 0xEDACE980u) ^ 0x8AE7DC65u;
                    return new Il2CppFieldRef { TypeIndex = unchecked((int)(U32(d, 0) + key + 0x5A999460u)), FieldIndex = unchecked((int)(U32(d, 4) + key + 0x39293070u)) };
                }
            );
            Array.Sort(usages, (a, b) => a.VirtualAddress.CompareTo(b.VirtualAddress));
            Usages.EnsureCapacity(usages.Length);
            foreach (var usage in usages)
            {
                if (!usage.IsValid)
                    continue;
                if (Usages.Count > 0 && Usages[^1].VirtualAddress == usage.VirtualAddress)
                {
                    if (Usages[^1].Type != usage.Type || Usages[^1].SourceIndex != usage.SourceIndex)
                        throw new InvalidDataException("Conflicting BH3 metadata usage slots.");
                    continue;
                }
                Usages.Add(usage);
            }
            foreach (var cache in caches.OrderBy(c => c.Key))
                RuntimeCaches.Add((cache.Value, cache.Key));
        }
    }
}
