using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;
using static Il2CppInspector.MoraxReader;

namespace Il2CppInspector;

internal sealed partial class ZzzMorax
{
    public override List<MetadataUsage> Usages { get; } = [];
    internal List<(int TypeIndex, ulong Address)> RuntimeCaches { get; } = [];
    public override IEnumerable<Plugins.AdditionalMetadataUsage> AdditionalUsages =>
        RuntimeCaches.Select(cache => new Plugins.AdditionalMetadataUsage(
            "moraxRuntimeCaches",
            cache.TypeIndex,
            cache.Address,
            $"RuntimeCache_{cache.TypeIndex}_{cache.Address:X}",
            "void *"
        ));
    internal uint[] RawFieldOffsets { get; private set; }
    internal Dictionary<int, ulong> GenericAdjustorThunks { get; } = [];

    internal (int Definition, int Instance) GenericClass(ulong ordinal)
    {
        if (ordinal >= (uint)GenericClassCount)
        {
            throw new InvalidDataException("ZZZ generic class ordinal out of range.");
        }

        var offset = checked(Slot(0x014) + (int)ordinal * 8);
        return (I32(startup, offset + 4), I32(startup, offset));
    }

    // The header stores the generic class table size in bytes; entries are eight bytes.
    internal int GenericClassCount => Slot(0x094) >> 3;

    private void ReadBinary(Il2CppBinary binary)
    {
        var typesVa = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x08);
        var typeCount = checked((int)(Image.ReadMappedUInt32(MetadataRegistrationAddress + 0x40) ^ 0x3745AAFCu));
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
        var instCount = checked((int)unchecked(Image.ReadMappedUInt32(MetadataRegistrationAddress + 0x54) - 0x5CC8ED8Du));
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
        var instances = ImmutableCollectionsMarshal.AsImmutableArray(insts);
        var specs = Records(
            global,
            Base(0x14C),
            Slot(0x164) / 12,
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
        RawFieldOffsets = new uint[Metadata.Fields.Length];
        var normalizedOffsets = new uint[RawFieldOffsets.Length];
        for (var i = 0; i < sizes.Length; i++)
        {
            var type = Metadata.Types[i];
            var group = checked((int)U32(global, Base(0x0AC) + i * 4));
            sizes[i] = LayoutSizes(group);
            var offsetIndex = checked((int)U32(global, Base(0x28) + group * 12 + 8));
            for (var f = 0; f < type.FieldCount; f++)
            {
                var fi = type.FieldIndex + f;
                var raw = U32(global, Base(0x10C) + (offsetIndex + f) * 4);
                RawFieldOffsets[fi] = raw;
                normalizedOffsets[fi] = DecodeFieldOffset(raw);
            }
        }
        var layoutTypes = Slot(0x0E0);
        var layoutGroups = Slot(0x108);
        for (var i = 0; i < Slot(0x0C0); i++)
        {
            var type = types[U32(startup, layoutTypes + i * 4)];
            if (type.Type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
            {
                GenericLayoutGroups.TryAdd(checked((int)type.Data.Value), checked((int)U32(startup, layoutGroups + i * 4)));
            }
        }
        Attach(
            binary,
            Metadata,
            ImmutableCollectionsMarshal.AsImmutableArray(types),
            typeAddresses,
            instances,
            specs,
            ImmutableCollectionsMarshal.AsImmutableArray(normalizedOffsets),
            ImmutableCollectionsMarshal.AsImmutableArray(sizes)
        );
        var cr = CodeRegistrationAddress;
        binary.CodeRegistration = new()
        {
            CodeGenModules = Image.ReadMappedUInt64(cr + 0x78),
            CodeGenModulesCount = (uint)Metadata.Images.Length,
            CustomAttributeCount = unchecked((int)(Image.ReadMappedUInt32(cr + 0x38) ^ 0x1D8DC73Fu)),
            CustomAttributeGenerators = Image.ReadMappedUInt64(cr + 0x68),
            InvokerPointersCount = unchecked(Image.ReadMappedUInt32(cr + 0x60) - 0x3E0AF451u),
            InvokerPointers = Image.ReadMappedUInt64(cr + 0x70),
            GenericMethodPointersCount = unchecked(Image.ReadMappedUInt32(cr + 0x3C) - 0x54AB29B6u),
            GenericMethodPointers = Image.ReadMappedUInt64(cr + 0x08),
            GenericAdjustorThunks = Image.ReadMappedUInt64(cr + 0x50),
            ReversePInvokeWrappers = Image.ReadMappedUInt64(cr + 0x18),
            InteropDataCount = unchecked(Image.ReadMappedUInt32(cr + 0x48) - 0x299BB03Eu),
            InteropData = Image.ReadMappedUInt64(cr + 0x20),
            UnresolvedVirtualCallCount = unchecked((int)(Image.ReadMappedUInt32(cr + 0x44) ^ 0x0962FEDCu)),
            UnresolvedVirtualCallPointers = Image.ReadMappedUInt64(cr + 0x10),
        };
        binary.MetadataRegistration = new()
        {
            TypesCount = types.Length,
            Types = typesVa,
            GenericInstsCount = instances.Length,
            GenericInsts = instsVa,
            GenericClassesCount = GenericClassCount,
            MethodSpecsCount = specs.Length,
            FieldOffsetsCount = Metadata.Types.Length,
            TypeDefinitionsSizesCount = Metadata.Types.Length,
        };
        binary.CustomAttributeGenerators = Image.ReadMappedUWordArray(
            binary.CodeRegistration.CustomAttributeGenerators,
            binary.CodeRegistration.CustomAttributeCount
        );
        binary.MethodInvokePointers = Image.ReadMappedUWordArray(
            binary.CodeRegistration.InvokerPointers,
            checked((int)binary.CodeRegistration.InvokerPointersCount)
        );
        if (binary.CustomAttributeGenerators.Length != Metadata.AttributeTypeRanges.Length)
        {
            throw new InvalidDataException("ZZZ attribute range/generator count mismatch.");
        }

        var moduleCount = checked((int)unchecked(Image.ReadMappedUInt32(CodeRegistrationAddress + 0x58) - 0x55E16FF9u));
        if (moduleCount != Metadata.Images.Length)
        {
            throw new InvalidDataException("ZZZ module/image count mismatch.");
        }

        var modulesVa = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x78);
        uint reversePInvokeCount = 0;
        for (var i = 0; i < moduleCount; i++)
        {
            var image = Metadata.Images[i];
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
            // +18 is the encoded method pointer count; it equals the token range in all 170 modules.
            if ((Image.ReadMappedUInt32(moduleVa + 0x18) ^ 0x01C7B040u) != count)
            {
                throw new InvalidDataException("ZZZ module method count/token range mismatch.");
            }

            var pointersVa = Image.ReadMappedUInt64(moduleVa + 0x38);
            var pointers =
                count == 0 ? []
                : pointersVa != 0 && Image.TryMapVATR(pointersVa, out _) ? Image.ReadMappedUWordArray(pointersVa, count)
                : new ulong[count];
            // Some entries are generic instances, not code; keep only executable addresses.
            for (var m = 0; m < pointers.Length; m++)
            {
                if (!IsCode(pointers[m]))
                {
                    pointers[m] = 0;
                }
            }
            // Initialization uses the module's stored index, not its ordinal in codeGenModules (0x180289B81).
            var moduleIndex = unchecked(Image.ReadMappedUInt32(moduleVa) ^ 0x1B557719u);
            if (moduleIndex >= moduleCount)
            {
                throw new InvalidDataException("ZZZ module invoker index out of range.");
            }

            var slice = checked((int)(U32(global, Base(0x0C4) + checked((int)moduleIndex) * 4) & 0xFFFFFFFCu));
            var invokers = Records(global, Base(0x13C) + slice, count, 4, (d, _) => I32(d, 0));
            if (invokers.Any(index => index < -1 || index >= binary.MethodInvokePointers.Length))
            {
                throw new InvalidDataException("ZZZ method invoker outside registered table.");
            }

            var name = Metadata.Strings[image.NameIndex];
            var moduleName = Image.ReadMappedUInt64(moduleVa + 0x10);
            if (Image.ReadMappedNullTerminatedString(moduleName) != name)
            {
                throw new InvalidDataException("ZZZ code generation module name mismatch.");
            }

            var module = new Il2CppCodeGenModule
            {
                ModuleName = moduleName,
                MethodPointerCount = (uint)count,
                MethodPointers = pointersVa,
                AdjustorThunksCount = unchecked(Image.ReadMappedUInt32(moduleVa + 0x04) ^ 0x4E75B561u),
                AdjustorThunks = Image.ReadMappedUInt64(moduleVa + 0x20),
                ReversePInvokeWrapperCount = Image.ReadMappedUInt32(moduleVa + 0x48) ^ 0x460E780Fu,
                ReversePInvokeWrapperIndices = Image.ReadMappedUInt64(moduleVa + 0x58),
                RgctxRangesCount = unchecked(Image.ReadMappedUInt32(moduleVa + 0x4C) - 0x4B2A0B2Du),
                RgctxRanges = Image.ReadMappedUInt64(moduleVa + 0x30),
                RgctxsCount = unchecked(Image.ReadMappedUInt32(moduleVa + 0x28) - 0x58545BC3u),
                Rgctxs = Image.ReadMappedUInt64(moduleVa + 0x40),
            };
            // The stored definition count agrees with the largest token-range end in all 170 modules.
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

            // Native tuples contain {token, wrapper index, usage index, method spec index}.
            // Recover the wrapper-table extent from its module references.
            for (var wrapper = 0u; wrapper < module.ReversePInvokeWrapperCount; wrapper++)
            {
                var index = unchecked((int)Image.ReadMappedUInt32(module.ReversePInvokeWrapperIndices + wrapper * 16UL + 4));
                if (index < -1)
                    throw new InvalidDataException("ZZZ reverse P/Invoke wrapper index out of range.");
                if (index >= 0)
                    reversePInvokeCount = Math.Max(reversePInvokeCount, checked((uint)index + 1));
            }

            binary.Modules.Add(name, module);
            binary.CodeGenModulePointers.Add(name, moduleVa);
            binary.ModuleMethodPointers.Add(module, pointers);
            binary.MethodInvokerIndices.Add(module, invokers);
        }
        var registration = binary.CodeRegistration;
        registration.ReversePInvokeWrapperCount = reversePInvokeCount;
        binary.CodeRegistration = registration;

        var genericVa = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x08);
        var genericCount = Image.ReadMappedUInt32(CodeRegistrationAddress + 0x3C) - 0x54AB29B6u;
        var adjustorsVa = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x50);
        for (var i = 0; i < Slot(0x170) / 16; i++)
        {
            var o = Base(0x04C) + i * 16;
            var specIndex = U32(global, o);
            var invokerIndex = U32(global, o + 4);
            var pointerIndex = U32(global, o + 8);
            var adjustor = U32(global, o + 12);
            if (specIndex >= specs.Length || pointerIndex >= genericCount || invokerIndex >= binary.MethodInvokePointers.Length)
            {
                throw new InvalidDataException("ZZZ generic method table index out of range.");
            }

            var spec = specs[(int)specIndex];
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

            binary.GenericMethodInvokerIndices.TryAdd(spec, (int)invokerIndex);
        }
        ReadUsages();
    }

    public override Il2CppTypeDefinitionSizes LayoutSizes(int group)
    {
        var o = checked(Base(0x28) + group * 12);
        var instanceSize = U16(global, o + 4);
        var nativeSizes = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x90);
        var nativeSize = Image.ReadMappedUInt16(nativeSizes + checked((ulong)group * 2));
        return new()
        {
            InstanceSize = instanceSize,
            NativeSize = nativeSize == ushort.MaxValue ? -1 : nativeSize,
            StaticFieldsSize = U16(global, o),
            ThreadStaticFieldsSize = U16(global, o + 2),
        };
    }

    internal override int LayoutAlignment(int group)
    {
        // Class setup computes log2 from descriptor +6 for instance alignment.
        var alignment = global[checked(Base(0x28) + group * 12 + 6)];
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
        var start = checked((int)U32(global, Base(0x28) + group * 12 + 8));
        return U32(global, Base(0x10C) + (start + local) * 4);
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

    internal override int DefinitionLayoutGroup(int definition) => checked((int)U32(global, Base(0x0AC) + definition * 4));

    private void ReadUsages()
    {
        var last = unchecked((int)(Image.ReadMappedUInt32(UsageAddress + 0x18) + 0x9D460EEAu));
        if (last == -1)
        {
            Metadata.FieldRefs = [];
            return;
        }

        var sentinel = checked(last + 1);
        var listOffsets = Base(0x084);
        CheckRange(global, listOffsets, checked((sentinel + 1) * 4));
        var count = checked(
            (int)(
                unchecked((uint)sentinel * 0xC02AB7FBu - 0x3433DE40u) ^ unchecked(U32(global, listOffsets + sentinel * 4) - 0x65DFD6F4u) ^ 0x2E019966u
            )
        );
        var pairs = Base(0x16C);
        CheckRange(global, pairs, checked(count * 8));
        var usages = new MetadataUsage[count];
        var fieldRefs = 0;
        // Destination tables indexed by usage kind; kind 2 has none and its usages are skipped.
        ulong[] tables =
        [
            0,
            Image.ReadMappedUInt64(UsageAddress + 0x20),
            0,
            Image.ReadMappedUInt64(UsageAddress + 0x40),
            Image.ReadMappedUInt64(UsageAddress + 0x50),
            Image.ReadMappedUInt64(UsageAddress + 0x10),
            Image.ReadMappedUInt64(UsageAddress + 0x40),
            Image.ReadMappedUInt64(UsageAddress + 0x48),
        ];
        for (var i = 0; i < count; i++)
        {
            var key = UsagePairKey(i);
            var o = pairs + i * 8;
            // The source token lives in the second dword; the destination slot in the first.
            var source = unchecked(U32(global, o + 4) ^ 0x2790DCE0u) - key;
            var destination = unchecked((int)(U32(global, o) - key - 0x3719779Au));
            var kind = source >> 29;
            var index = checked((int)(source & 0x1FFFFFFF));
            if (kind == 7)
            {
                // 0x1802A3850 creates cached payloads keyed by the source type.
                RuntimeCaches.Add((index, tables[7] + (ulong)destination * 8UL));
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

            var table = tables[kind];
            if (table == 0)
            {
                continue;
            }

            usages[i] = new((MetadataUsageType)kind, index, table + (ulong)destination * 8UL);
        }
        Metadata.FieldRefs = Records(global, Base(0x190), fieldRefs, 8, Il2CppFieldRefReader.FromZzz);
        // Multiple lists can initialize the same slot; sort by address to de-duplicate without another large hash table.
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

    private static uint UsagePairKey(int i) =>
        unchecked((uint)((0x2CD690BAUL * ((0xF588148176DUL * (ulong)i + 0x1A37C4E843A11UL) ^ 0x2DDF3980UL)) >> 0x13) ^ 0x3EA2FB42u);
}
