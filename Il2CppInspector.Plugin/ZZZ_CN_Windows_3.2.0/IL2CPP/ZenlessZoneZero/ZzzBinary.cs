using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector
{
    internal sealed partial class ZzzMorax
    {
        internal ImmutableArray<Il2CppGenericInst> GenericInstances;
        public override List<MetadataUsage> Usages { get; } = [];
        internal List<(int TypeIndex, ulong Address)> RuntimeCaches { get; } = [];
        public override IEnumerable<Plugins.AdditionalMetadataUsage> AdditionalUsages =>
            RuntimeCaches.Select(cache => new Plugins.AdditionalMetadataUsage("moraxRuntimeCaches", cache.TypeIndex, cache.Address, $"RuntimeCache_{cache.TypeIndex}_{cache.Address:X}", "void *"));
        private uint[] rawFieldOffsets;
        internal uint[] RawFieldOffsets => rawFieldOffsets;
        internal Dictionary<int, int> GenericLayoutGroups { get; } = [];
        internal Dictionary<int, ulong> GenericAdjustorThunks { get; } = [];

        internal (int Definition, int Instance) GenericClass(ulong ordinal)
        {
            if (ordinal >= (uint)Slot(0x108))
            {
                throw new InvalidDataException("ZZZ generic class ordinal out of range.");
            }

            var offset = checked(Slot(0x15C) + (int)ordinal * 8);
            return (I32(startup, offset + 4), I32(startup, offset));
        }

        private void ReadBinary(Il2CppBinary binary)
        {
            var typesVa = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x30);
            var typeCount = checked((int)(Image.ReadMappedUInt32(MetadataRegistrationAddress + 0x1C) ^ 0x56478D25u));
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
                    throw new InvalidDataException("Unverified ZZZ Il2CppType flag bits.");
                }

                types[i] = new()
                {
                    Data = new() { Value = U64(typeBytes, o) },
                    Value = U16(typeBytes, o + 8) | (uint)typeBytes[o + 10] << 16 | (uint)(bits & 0x40) << 23,
                };
                typeAddresses.Add(typesVa + (ulong)o, i);
            }
            var instCount = checked((int)unchecked(Image.ReadMappedUInt32(MetadataRegistrationAddress + 0x3C) + 0xF0B312DFu));
            var instsVa = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x48);
            var instBytes = Image.ReadMappedBytes(instsVa, checked(instCount * 16));
            var insts = new Il2CppGenericInst[instCount];
            for (var i = 0; i < instCount; i++)
            {
                var argc = U32(instBytes, i * 16);
                if (argc is 0 or > 64)
                {
                    throw new InvalidDataException("Invalid ZZZ generic argument count.");
                }

                insts[i] = new() { TypeArgc = argc, TypeArgv = U64(instBytes, i * 16 + 8) };
            }
            GenericInstances = ImmutableCollectionsMarshal.AsImmutableArray(insts);
            var specs = Records(
                global,
                Base(0x144),
                Slot(0x018) / 12,
                12,
                (d, _) =>
                    new Il2CppMethodSpec
                    {
                        ClassIndexIndex = I32(d, 0),
                        MethodDefinitionIndex = I32(d, 4),
                        MethodIndexIndex = I32(d, 8),
                    }
            );
            // 0x18028DF60 uses the ordinary metadata-usage encoding here; zero is an empty slot.
            foreach (var entry in Metadata.VTableMethodIndices)
            {
                if (entry == 0)
                {
                    continue;
                }

                var kind = entry >> 29;
                var index = entry & 0x1FFFFFFF;
                if (index == 0 || (kind != 3 && kind != 6) || (kind == 3 ? index >= Metadata.Methods.Length : index >= specs.Length))
                {
                    throw new InvalidDataException("ZZZ vtable method reference outside decoded tables.");
                }
            }
            var sizes = new Il2CppTypeDefinitionSizes[Metadata.Types.Length];
            rawFieldOffsets = new uint[Metadata.Fields.Length];
            var normalizedOffsets = new uint[RawFieldOffsets.Length];
            for (var i = 0; i < sizes.Length; i++)
            {
                var type = Metadata.Types[i];
                var group = checked((int)U32(global, Base(0x190) + i * 4));
                sizes[i] = LayoutSizes(group);
                var offsetIndex = checked((int)U32(global, Base(0x174) + group * 12 + 8));
                for (var f = 0; f < type.FieldCount; f++)
                {
                    var raw = U32(global, Base(0x140) + (offsetIndex + f) * 4);
                    RawFieldOffsets[type.FieldIndex + f] = raw;
                }
                for (var f = 0; f < type.FieldCount; f++)
                {
                    var fi = type.FieldIndex + f;
                    var raw = RawFieldOffsets[fi];
                    normalizedOffsets[fi] = DecodeFieldOffset(raw);
                }
            }
            var layoutTypes = Slot(0x08C);
            var layoutGroups = Slot(0x048);
            for (var i = 0; i < Slot(0x0C0) / 4; i++)
            {
                var type = types[U32(startup, layoutTypes + i * 4)];
                if (type.Type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
                {
                    GenericLayoutGroups.TryAdd(checked((int)type.Data.Value), checked((int)U32(startup, layoutGroups + i * 4)));
                }
            }
            binary.InitializeZzz(
                this,
                ImmutableCollectionsMarshal.AsImmutableArray(types),
                typeAddresses,
                GenericInstances,
                specs,
                ImmutableCollectionsMarshal.AsImmutableArray(normalizedOffsets),
                ImmutableCollectionsMarshal.AsImmutableArray(sizes)
            );

            var moduleCount = unchecked(Image.ReadMappedUInt32(CodeRegistrationAddress + 0x28) + 0xC5EB8132u);
            if (moduleCount != Metadata.Images.Length)
            {
                throw new InvalidDataException("ZZZ module/image count mismatch.");
            }

            var modulesVa = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x20);
            var typeImages = new int[Metadata.Types.Length];
            Array.Fill(typeImages, -1);
            for (var i = 0; i < moduleCount; i++)
            {
                var image = Metadata.Images[i];
                for (var t = (int)image.TypeStart; t < image.TypeStart + image.TypeCount; t++)
                {
                    typeImages[t] = i;
                }

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
                // +3C is an encoded length; token row-id selects pointer[row-id - 1] (0x18027DBBE).
                var decodedCount = unchecked(Image.ReadMappedUInt32(moduleVa + 0x3C) - 0x1F6AEAF7u);
                if (decodedCount != count)
                {
                    throw new InvalidDataException("ZZZ module method count/token range mismatch.");
                }

                var pointersVa = Image.ReadMappedUInt64(moduleVa + 0x48);
                var pointers =
                    count == 0 ? []
                    : pointersVa != 0 && Image.TryMapVATR(pointersVa, out _) ? Image.ReadMappedUWordArray(pointersVa, count)
                    : new ulong[count];
                // Some entries represent generic instances rather than code. Keep only executable addresses.
                for (var m = 0; m < pointers.Length; m++)
                {
                    if (!IsCode(pointers[m]))
                    {
                        pointers[m] = 0;
                    }
                }
                // Initialization uses the module's stored index, not its ordinal in codeGenModules (0x18026EFF0).
                var moduleIndex = unchecked(Image.ReadMappedUInt32(moduleVa + 0x28) - 0x2A6FF452u);
                if (moduleIndex >= moduleCount)
                {
                    throw new InvalidDataException("ZZZ module invoker index out of range.");
                }

                var slice = checked((int)U32(global, Base(0x014) + checked((int)moduleIndex) * 4));
                var invokers = Records(global, Base(0x028) + slice, count, 4, (d, _) => I32(d, 0));
                if (invokers.Any(index => index < -1 || index >= binary.MethodInvokePointers.Length))
                {
                    throw new InvalidDataException("ZZZ method invoker outside registered table.");
                }

                var name = Metadata.Strings[image.NameIndex];
                var moduleName = Image.ReadMappedUInt64(moduleVa + 8);
                if (Image.ReadMappedNullTerminatedString(moduleName) != name)
                {
                    throw new InvalidDataException("ZZZ code generation module name mismatch.");
                }

                var module = new Il2CppCodeGenModule
                {
                    ModuleName = moduleName,
                    MethodPointerCount = decodedCount,
                    MethodPointers = pointersVa,
                    AdjustorThunksCount = unchecked(Image.ReadMappedUInt32(moduleVa + 0x14) - 0x04CA7E30u),
                    AdjustorThunks = Image.ReadMappedUInt64(moduleVa + 0x40),
                    ReversePInvokeWrapperCount = unchecked(Image.ReadMappedUInt32(moduleVa + 0x10) - 0x023B42CAu),
                    ReversePInvokeWrapperIndices = Image.ReadMappedUInt64(moduleVa + 0x20),
                    RgctxRangesCount = Image.ReadMappedUInt32(moduleVa + 0x38) ^ 0x153920C2u,
                    RgctxRanges = Image.ReadMappedUInt64(moduleVa + 0x30),
                    RgctxsCount = Image.ReadMappedUInt32(moduleVa + 0x18) ^ 0x1ABD7223u,
                    Rgctxs = Image.ReadMappedUInt64(moduleVa + 0x50),
                };
                // All 170 stored definition counts equal the largest token-range end in this build.
                // The runtime lookup consumes ranges; no independent count consumer has been found.
                uint rgctxEnd = 0;
                for (var rangeIndex = 0u; rangeIndex < module.RgctxRangesCount; rangeIndex++)
                {
                    var rangeVa = module.RgctxRanges + rangeIndex * 12UL;
                    var start = Image.ReadMappedUInt32(rangeVa + 4);
                    var length = Image.ReadMappedUInt32(rangeVa + 8);
                    rgctxEnd = Math.Max(rgctxEnd, checked(start + length));
                }
                if (rgctxEnd != module.RgctxsCount)
                {
                    throw new InvalidDataException("ZZZ RGCTX definition count/token range mismatch.");
                }

                binary.Modules.Add(name, module);
                binary.CodeGenModulePointers.Add(name, moduleVa);
                binary.ModuleMethodPointers.Add(module, pointers);
                binary.MethodInvokerIndices.Add(module, invokers);
            }
            var genericVa = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x70);
            var genericCount = Image.ReadMappedUInt32(CodeRegistrationAddress + 0x5C) ^ 0x5E455406u;
            var adjustorsVa = Image.ReadMappedUInt64(CodeRegistrationAddress);
            for (var i = 0; i < Slot(0x178) / 16; i++)
            {
                var o = Base(0x050) + i * 16;
                var specIndex = U32(global, o + 8);
                var pointerIndex = U32(global, o + 12);
                if (specIndex >= specs.Length || pointerIndex >= genericCount)
                {
                    throw new InvalidDataException("ZZZ generic method table index out of range.");
                }

                var spec = specs[(int)specIndex];
                var adjustor = U32(global, o);
                var pointer = Image.ReadMappedUInt64(genericVa + pointerIndex * 8UL);
                var thunk = adjustor == uint.MaxValue ? 0 : Image.ReadMappedUInt64(adjustorsVa + adjustor * 8UL);
                if (IsCode(thunk))
                {
                    GenericAdjustorThunks.TryAdd((int)specIndex, thunk);
                }

                if (IsCode(pointer))
                {
                    binary.GenericMethodPointers.TryAdd(spec, pointer);
                }

                binary.GenericMethodInvokerIndices.TryAdd(spec, I32(global, o + 4));
            }
            ReadUsages();
        }

        public override Il2CppTypeDefinitionSizes LayoutSizes(int group)
        {
            var o = checked(Base(0x174) + group * 12);
            var instanceSize = U16(global, o + 4);
            var nativeSizes = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x90);
            var nativeSize = Image.ReadMappedUInt16(nativeSizes + checked((ulong)group * 2));
            return new()
            {
                InstanceSize = instanceSize,
                // Class +C0 is the marshal size, not managed storage (e.g. Boolean: 4 vs 1).
                NativeSize = nativeSize == ushort.MaxValue ? -1 : nativeSize,
                StaticFieldsSize = U16(global, o),
                ThreadStaticFieldsSize = U16(global, o + 2),
            };
        }

        internal int LayoutAlignment(int group)
        {
            // Class setup computes log2 from descriptor +6 for instance alignment.
            // Descriptor +2 is the thread-static storage size (class +C9).
            var alignment = global[checked(Base(0x174) + group * 12 + 6)];
            if (alignment is not (1 or 2 or 4 or 8 or 16))
            {
                throw new InvalidDataException($"Invalid ZZZ instance alignment {alignment} in layout group {group}.");
            }

            return alignment;
        }

        private static uint DecodeFieldOffset(uint raw) =>
            (raw >> 24) switch
            {
                1 => (raw & 0xFFFFFF) | 0x80000000,
                // The static accessors select process-wide buffers for tags 2 and 4. Keep their offsets.
                2 or 4 => raw & 0xFFFFFF,
                _ => raw,
            };

        internal uint LayoutRawFieldOffset(int group, int local)
        {
            var start = checked((int)U32(global, Base(0x174) + group * 12 + 8));
            return U32(global, Base(0x140) + (start + local) * 4);
        }

        public override uint LayoutFieldOffset(int group, int local) => DecodeFieldOffset(LayoutRawFieldOffset(group, local));

        internal bool DefinitionHasSplitVTable(Next.Metadata.Il2CppTypeDefinition definition)
        {
            for (var i = 0; i < definition.VTableCount; i++)
            {
                var value = Metadata.VTableMethodIndices[definition.VTableIndex + i];
                if (value >> 29 == (uint)MetadataUsageType.MethodRef && (value & 0x1FFFFFFF) != 0)
                {
                    return true;
                }
            }
            return false;
        }

        internal int DefinitionLayoutGroup(int definition) => checked((int)U32(global, Base(0x190) + definition * 4));

        internal int GenericClassCount => Slot(0x108);

        private void ReadUsages()
        {
            var last = checked((int)(Image.ReadMappedUInt32(UsageAddress + 0x38) ^ 0x3C27E425u));
            var sentinel = checked(last + 1);
            var count = checked((int)(unchecked((uint)sentinel * 0x3E2914E4u + 0x33400EF1u) ^ unchecked(U32(global, Base(0x188) + sentinel * 4) + 0xE9FC39D1u) ^ 0x67A28B2Cu));
            CheckRange(global, Base(0x118), checked(count * 8));
            var usages = new MetadataUsage[count];
            var fieldRefs = 0;
            for (var i = 0; i < count; i++)
            {
                var scaled = unchecked(((ulong)i * 0xEC3DUL ^ 0x37565921UL) * 0x45297162UL) >> 11;
                var key = unchecked((uint)((scaled * 0x20E72761UL + 0x1A4298246D5DCAUL) >> 21));
                var o = Base(0x118) + i * 8;
                var source = key ^ unchecked(U32(global, o) + 0xFC38BD15u);
                var destination = key ^ unchecked(U32(global, o + 4) + 0xB3E0885Bu);
                var kind = source >> 29;
                var index = checked((int)(source & 0x1FFFFFFF));
                if (kind == 7)
                {
                    // sub_18028EB20 creates different cached payloads according to destination bit 0.
                    // The source is a type reference, not a stock FieldRva index.
                    RuntimeCaches.Add((index, Image.ImageBase + 0x538D140 + destination * 8UL));
                    continue;
                }
                if (kind is < 1 or > 6)
                {
                    throw new InvalidDataException("Unexpected ZZZ metadata usage kind.");
                }

                if (kind == 4)
                {
                    fieldRefs = Math.Max(fieldRefs, index + 1);
                }

                var address = Image.ImageBase + UsageTableRvas[kind] + destination * 8UL;
                usages[i] = new((MetadataUsageType)kind, index, address);
            }
            Metadata.FieldRefs = Records(global, Base(0x0BC), fieldRefs, 8, Il2CppFieldRefReader.FromZzz);
            // Multiple lists can initialize the same slot. De-duplicate by address without a second multi-million-entry hash table.
            Array.Sort(usages, (a, b) => a.VirtualAddress.CompareTo(b.VirtualAddress));
            Usages.EnsureCapacity(usages.Length);
            foreach (var usage in usages)
            {
                if (!usage.IsValid)
                {
                    continue;
                }

                if (Usages.Count > 0 && Usages[^1].VirtualAddress == usage.VirtualAddress)
                {
                    if (Usages[^1].Type != usage.Type || Usages[^1].SourceIndex != usage.SourceIndex)
                    {
                        throw new InvalidDataException("Conflicting ZZZ metadata usages for the same address.");
                    }

                    continue;
                }
                Usages.Add(usage);
            }
        }

        // The resolver uses these build-specific linked BSS arenas, not the stock metadataUsages pointer array.
        private static readonly uint[] UsageTableRvas = [0, 0x4F826E0, 0x538D130, 0x509BDA0, 0x52E47E0, 0x52E67C0, 0x509BDA0];
    }
}
