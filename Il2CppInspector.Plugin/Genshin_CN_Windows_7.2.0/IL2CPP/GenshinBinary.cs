using System.Runtime.InteropServices;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;
using Il2CppInspector.Reflection;
using static Il2CppInspector.MoraxReader;

namespace Il2CppInspector;

internal sealed partial class GenshinMorax
{
    public override List<MetadataUsage> Usages { get; } = [];
    public override IEnumerable<Plugins.AdditionalMetadataUsage> AdditionalUsages => runtimeCaches;
    private readonly List<Plugins.AdditionalMetadataUsage> runtimeCaches = [];
    private uint[] rawFieldOffsets;
    internal int GenericClassCount => Slot(0x44) / 8;

    internal (int Definition, int Instance) GenericClass(ulong ordinal)
    {
        if (ordinal >= (uint)GenericClassCount)
            throw new InvalidDataException("Generic class ordinal out of range.");
        var o = checked(Slot(0xD0) + (int)ordinal * 8);
        // 0x14053F45D copies +4 to the definition; +0 indexes MR.genericInsts.
        return (I32(startup, o + 4), I32(startup, o));
    }

    private void ReadBinary(Il2CppBinary binary)
    {
        var mr = MetadataRegistrationAddress;
        var cr = CodeRegistrationAddress;
        var typesVa = Image.ReadMappedUInt64(mr + 0x00);
        var typeCount = checked((int)(unchecked(Image.ReadMappedUInt32(mr + 0x8C) + 0xCE67355Cu)));
        var bytes = Image.ReadMappedBytes(typesVa, checked(typeCount * 16));
        var types = new Il2CppType[typeCount];
        var addresses = new Dictionary<ulong, int>(typeCount);
        for (var i = 0; i < typeCount; i++)
        {
            var o = i * 16;
            var bits = bytes[o + 11];
            if ((bits & ~0x40) != 0)
                throw new InvalidDataException("Unverified Il2CppType flag bits.");
            types[i] = new()
            {
                Data = new() { Value = U64(bytes, o) },
                Value = U16(bytes, o + 8) | (uint)bytes[o + 10] << 16 | (uint)(bits & 0x40) << 23,
            };
            addresses.Add(typesVa + (ulong)o, i);
        }
        var instsVa = Image.ReadMappedUInt64(mr + 0x70);
        var instCount = checked((int)(unchecked(Image.ReadMappedUInt32(mr + 0x08) + 0x82A2CEC7u)));
        Image.Version = NominalMetadataVersion;
        var instances = Image.ReadMappedVersionedObjectArray<Il2CppGenericInst>(instsVa, instCount);
        var specs = Records(
            global,
            Base(0x170),
            Slot(0x24) / 12,
            12,
            (d, _) =>
                new Il2CppMethodSpec
                {
                    ClassIndexIndex = I32(d, 4),
                    MethodIndexIndex = I32(d, 8),
                    MethodDefinitionIndex = I32(d, 0),
                }
        );
        if (
            specs.Any(s =>
                (uint)s.MethodDefinitionIndex >= Metadata.Methods.Length
                || s.ClassIndexIndex < -1
                || s.ClassIndexIndex >= instances.Length
                || s.MethodIndexIndex < -1
                || s.MethodIndexIndex >= instances.Length
            )
        )
            throw new InvalidDataException("MethodSpec index out of range.");
        var sizes = new Il2CppTypeDefinitionSizes[Metadata.Types.Length];
        rawFieldOffsets = new uint[Metadata.Fields.Length];
        var offsets = new uint[rawFieldOffsets.Length];
        for (var i = 0; i < sizes.Length; i++)
        {
            var t = Metadata.Types[i];
            var group = DefinitionLayoutGroup(i);
            sizes[i] = LayoutSizes(group);
            for (var f = 0; f < t.FieldCount; f++)
            {
                var fi = t.FieldIndex + f;
                rawFieldOffsets[fi] = LayoutRawFieldOffset(group, f);
                offsets[fi] = DecodeFieldOffset(rawFieldOffsets[fi]);
            }
        }
        for (var i = 0; i < Slot(0x208) / 4; i++)
        {
            var ti = I32(startup, Slot(0x164) + i * 4);
            var group = I32(startup, Slot(0xB8) + i * 4);
            if ((uint)ti >= types.Length || types[ti].Type != Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST || (uint)group >= LayoutGroupCount)
                throw new InvalidDataException("Generic layout index out of range.");
            var ordinal = checked((int)types[ti].Data.Value);
            if (GenericLayoutGroups.TryGetValue(ordinal, out var previous) && previous != group)
                throw new InvalidDataException("Conflicting generic layout groups.");
            GenericLayoutGroups.TryAdd(ordinal, group);
        }
        Attach(
            binary,
            Metadata,
            ImmutableCollectionsMarshal.AsImmutableArray(types),
            addresses,
            instances,
            specs,
            ImmutableCollectionsMarshal.AsImmutableArray(offsets),
            ImmutableCollectionsMarshal.AsImmutableArray(sizes)
        );
        binary.Modules = null; // 24.1 addresses global methods directly.
        binary.CodeRegistration = new()
        {
            MethodPointers = Image.ReadMappedUInt64(cr + 0x70),
            MethodPointersCount = (uint)Metadata.Methods.Length,
            InvokerPointers = Image.ReadMappedUInt64(cr + 0x68),
            InvokerPointersCount = unchecked(Image.ReadMappedUInt32(cr + 0x90) + 0xF026D1B3u),
            GenericMethodPointers = Image.ReadMappedUInt64(cr + 0x30),
            GenericMethodPointersCount = unchecked(Image.ReadMappedUInt32(cr + 0x64) + 0x9EA91E3Bu),
            GenericAdjustorThunks = Image.ReadMappedUInt64(cr + 0x58),
            // 0x14052F805 indexes this array by AttributeTypeRange row.
            CustomAttributeGenerators = Image.ReadMappedUInt64(cr + 0x40),
            CustomAttributeCount = checked((int)(unchecked(Image.ReadMappedUInt32(cr + 0x50) + 0xF8614AF1u))),
        };
        binary.MetadataRegistration = new()
        {
            Types = typesVa,
            TypesCount = types.Length,
            GenericInsts = instsVa,
            GenericInstsCount = instances.Length,
            GenericClassesCount = GenericClassCount,
            MethodSpecsCount = specs.Length,
            FieldOffsetsCount = Metadata.Types.Length,
            TypeDefinitionsSizesCount = Metadata.Types.Length,
        };
        binary.MethodInvokePointers = Image.ReadMappedUWordArray(
            binary.CodeRegistration.InvokerPointers,
            checked((int)binary.CodeRegistration.InvokerPointersCount)
        );
        binary.CustomAttributeGenerators = Image.ReadMappedUWordArray(
            binary.CodeRegistration.CustomAttributeGenerators,
            binary.CodeRegistration.CustomAttributeCount
        );
        if (
            binary.CustomAttributeGenerators.Length != Metadata.AttributeTypeRanges.Length
            || Metadata.Methods.Any(m => m.InvokerIndex < -1 || m.InvokerIndex >= binary.MethodInvokePointers.Length)
        )
            throw new InvalidDataException("Invoker/custom attribute index out of range.");
        binary.GlobalMethodPointers = Image.ReadMappedUWordArray(binary.CodeRegistration.MethodPointers, Metadata.Methods.Length);
        // Value-type MethodInfo construction consults a separate {methodIndex,u16 pointerIndex} table.
        var valuePointers = Image.ReadMappedUInt64(cr + 0x88);
        for (var i = 0; i < Slot(0x48) / 6; i++)
        {
            var o = Base(0xA0) + i * 6;
            var method = I32(global, o);
            if ((uint)method >= Metadata.Methods.Length)
                throw new InvalidDataException("Value-type method override outside methods.");
            var owner = Metadata.Methods[method].DeclaringType;
            if (Metadata.Types[owner].Bitfield.ValueType)
            {
                var pointer = Image.ReadMappedUInt64(valuePointers + U16(global, o + 4) * 8UL);
                if (pointer != 0)
                    binary.GlobalMethodPointers[method] = pointer;
            }
        }
        for (var i = 0; i < binary.GlobalMethodPointers.Length; i++)
            if (!IsCode(binary.GlobalMethodPointers[i]))
                binary.GlobalMethodPointers[i] = 0;
        var genericCount = checked((int)binary.CodeRegistration.GenericMethodPointersCount);
        var genericPointers = Image.ReadMappedUWordArray(binary.CodeRegistration.GenericMethodPointers, genericCount);
        // 0x140541953/0x14052E646: {bodyIndex, specIndex, u16 adjustorIndex, invokerIndex}.
        for (var i = 0; i < Slot(0x110) / 14; i++)
        {
            var o = Base(0xEC) + i * 14;
            var specIndex = I32(global, o + 4);
            var pointerIndex = I32(global, o);
            var invoker = I32(global, o + 0xA);
            if (
                (uint)specIndex >= specs.Length
                || (uint)pointerIndex >= genericPointers.Length
                || (uint)invoker >= binary.MethodInvokePointers.Length
            )
                throw new InvalidDataException("Generic function index out of range.");
            var spec = specs[specIndex];
            var pointer = genericPointers[pointerIndex];
            if (IsCode(pointer))
                binary.GenericMethodPointers.TryAdd(spec, pointer);
            binary.GenericMethodInvokerIndices.TryAdd(spec, invoker);
        }
        ReadUsages(typeCount, specs.Length);
    }

    private int LayoutGroupCount => TableCount(0x14C, 0x170, 12);

    internal override int DefinitionLayoutGroup(int definition) => I32(global, Base(0x168) + definition * 4);

    public override Il2CppTypeDefinitionSizes LayoutSizes(int group)
    {
        if ((uint)group >= LayoutGroupCount)
            throw new InvalidDataException("Layout group out of range.");
        var o = Base(0x14C) + group * 12;
        var native = Image.ReadMappedUInt16(Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x68) + (ulong)group * 2);
        return new()
        {
            InstanceSize = U16(global, o + 4),
            NativeSize = native == 0xFFFF ? -1 : native,
            StaticFieldsSize = U16(global, o),
            ThreadStaticFieldsSize = global[o + 2],
        };
    }

    internal override int LayoutAlignment(int group) => global[Base(0x14C) + group * 12 + 6];

    internal uint LayoutRawFieldOffset(int group, int local)
    {
        var start = checked((int)U32(global, Base(0x14C) + group * 12 + 8));
        return U32(global, Base(0x154) + checked((start + local) * 4));
    }

    public override uint LayoutFieldOffset(int group, int local) => DecodeFieldOffset(LayoutRawFieldOffset(group, local));

    private static uint DecodeFieldOffset(uint raw) =>
        (raw >> 24) switch
        {
            1 => (raw & 0xFFFFFF) | 0x80000000,
            2 or 4 => raw & 0xFFFFFF,
            _ => raw,
        };

    internal bool DefinitionHasSplitVTable(Il2CppTypeDefinition d) =>
        ((U16(global, Base(0x64) + ((int)d.Token & 0xFFFFFF) * 70 + 0x32) ^ 0x7BF1) & 0x200) != 0;

    internal int DefinitionExtraVTableSlots(int index)
    {
        if (((U16(global, Base(0x64) + index * 70 + 0x32) ^ 0x7BF1) & 0x100) == 0)
            return 0;
        var lo = 0;
        var hi = (Slot(0x130) / 8);
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (I32(global, Base(0xC) + mid * 8) < index)
                lo = mid + 1;
            else
                hi = mid;
        }
        if (lo >= (Slot(0x130) / 8) || I32(global, Base(0xC) + lo * 8) != index)
            throw new InvalidDataException("Missing extra vtable layout.");
        return (global[Base(0xC) + lo * 8 + 7] ^ 0x27);
    }

    private void ReadUsages(int typeCount, int specCount)
    {
        var groups = TableCount(0x178, 0xC8, 4) - 1;
        uint Start(int i)
        {
            return unchecked((U32(global, Base(0x178) + i * 4) + 0x8BFF3E05u) ^ (0x5843EAA0u * (uint)i - 0x443C8630u));
        }
        var previous = Start(0);
        if (previous != 0)
            throw new InvalidDataException("Invalid usage origin.");
        for (var g = 1; g <= groups; g++)
        {
            var next = Start(g);
            if (next < previous)
                throw new InvalidDataException("Usage group order invalid.");
            previous = next;
        }
        var count = checked((int)previous);
        var bases = new ulong[8];
        foreach (var (kind, offset) in new[] { (0, 0x20), (1, 0x48), (3, 0x10), (4, 0x28), (5, 0x58), (6, 0x10), (7, 0x30) })
            bases[kind] = Image.ReadMappedUInt64(UsageAddress + (uint)offset);
        var limits = new[]
        {
            int.MaxValue,
            typeCount,
            0,
            Metadata.Methods.Length,
            Metadata.FieldRefs.Length,
            Metadata.StringLiterals.Length,
            specCount,
            typeCount,
        };
        var slots = new Dictionary<ulong, MetadataUsage>();
        for (var i = 0; i < count; i++)
        {
            var key = unchecked(0x57C15286u * (uint)i + 0x26DDEBDAu);
            var o = Base(0xF4) + i * 8;
            var source = unchecked(U32(global, o) + 0xABBDCABFu) ^ key ^ 0x2A357D90u;
            var dest = U32(global, o + 4) ^ key ^ 0x1855AA65u;
            var kind = source >> 29;
            var index = (int)(source & 0x1FFFFFFF);
            if (kind == 2 || index >= limits[kind])
                throw new InvalidDataException("Usage source out of range.");
            var address = checked(bases[kind] + (kind is 0 or 7 ? dest & 0xFFFFFFu : dest) * 8UL);
            if (kind == 0)
            {
                var text = DecodeString((dest & 0xFF000000) | (uint)index);
                runtimeCaches.Add(
                    new("moraxNativeStrings", -1, address, $"NativeString_{Cpp.CppDeclarationGenerator.FieldIdentifier(text)}", "char *")
                    {
                        Length = (int)(dest >> 24),
                        Value = text,
                    }
                );
                continue;
            }
            if (kind == 7)
            {
                if ((dest >> 24) != 0)
                    throw new InvalidDataException("Unknown array cache subtype.");
                runtimeCaches.Add(new("moraxEmptyArrays", index, address, $"EmptyArray_{index}", "Il2CppArray *") { Length = 0, Subtype = 0 });
                continue;
            }
            var usage = new MetadataUsage((MetadataUsageType)kind, index, address);
            if (slots.TryGetValue(address, out var old) && (old.Type != usage.Type || old.SourceIndex != index))
                throw new InvalidDataException("Conflicting usage slot.");
            slots.TryAdd(address, usage);
        }
        Usages.AddRange(slots.Values.OrderBy(u => u.VirtualAddress));
    }

    public override TypeInfo ResolveGenericType(TypeModel model, Il2CppType type)
    {
        var (definition, instance) = GenericClass(type.Data.Value);
        if ((uint)definition >= model.TypesByDefinitionIndex.Length || (uint)instance >= model.Package.GenericInstances.Length)
            throw new InvalidDataException("Generic class index out of range.");
        var result = model
            .TypesByDefinitionIndex[definition]
            .MakeGenericType(model.ResolveGenericArguments(model.Package.GenericInstances[instance]));
        if (!result.IsEnum && GenericLayoutGroups.TryGetValue(checked((int)type.Data.Value), out var group))
            result.SetGameLayoutGroup(group);
        return result;
    }

    public override int FieldStorageTag(FieldInfo field) =>
        (int)(
            (
                field.DeclaringType.GameLayoutGroup is int group
                    ? LayoutRawFieldOffset(group, field.Index - (int)field.DeclaringType.GetGenericTypeDefinition().Definition.FieldIndex)
                    : rawFieldOffsets[field.Index]
            ) >> 24
        );
}
