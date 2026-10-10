using System.Runtime.InteropServices;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;
using static Il2CppInspector.HkrpgRecords;

namespace Il2CppInspector;

internal sealed class HkrpgBinary
{
    internal IFileFormatStream Image { get; }
    internal HkrpgHeader Header { get; }
    internal ulong RegistrationAddress { get; }
    internal ulong CodeRegistrationAddress { get; }
    internal ulong MetadataRegistrationAddress { get; }
    internal ulong UsageAddress { get; }
    private readonly Section[] sections;
    private readonly EventHandler<string> status;
    private HkrpgMetadata metadata;

    internal IReadOnlyList<Il2CppType> Types => types;
    internal IReadOnlyList<Il2CppGenericInst> Instances => instances;
    internal IReadOnlyList<int[]> InstanceArguments => instanceArguments;
    internal IReadOnlyList<(int Definition, int Instance)> GenericClasses => genericClasses;

    internal HkrpgBinary(IFileFormatStream image, int metadataLength, EventHandler<string> status)
    {
        Image = image;
        this.status = status;
        if (image is not PEReader || image.Bits != 64 || image.Arch != "x64")
            throw new NotSupportedException("HSR 4.6.x supports Windows x64 PE images only.");
        sections = image.GetSections().Where(s => s.VirtualLength > 0).ToArray();
        var addresses = DiscoverRegistration(metadataLength);
        RegistrationAddress = addresses[0];
        CodeRegistrationAddress = addresses[1];
        MetadataRegistrationAddress = addresses[2];
        UsageAddress = addresses[3];
        Header = new HkrpgHeader(Image.ReadMappedBytes(addresses[5], 0x200), DiscoverPayloadOffset());
    }

    private readonly List<Il2CppType> types = [];
    private readonly List<Il2CppGenericInst> instances = [];
    private readonly List<int[]> instanceArguments = [];
    private (int Definition, int Instance)[] genericClasses;
    private ulong typesAddress;
    private ulong nativeSizesAddress;
    private int typeStride;
    private uint[] rawFieldOffsets;

    internal Il2CppBinary Load(HkrpgMetadata metadata, HkrpgUsages usages)
    {
        this.metadata = metadata;
        var binary = new Il2CppBinaryX64(Image, status);
        status?.Invoke(this, "Decoding HSR type references and generic instances");
        typesAddress = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x80);
        nativeSizesAddress = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x40);
        var probe = Image.ReadMappedBytes(typesAddress, 256);
        var votes8 = Enumerable.Range(0, 16).Count(i => probe[i * 8 + 6] is >= 1 and <= 0x1F);
        var votes16 = Enumerable.Range(0, 16).Count(i => probe[i * 16 + 10] is >= 1 and <= 0x1F);
        typeStride = votes16 >= votes8 ? 16 : 8;
        genericClasses = metadata.ReadStartup(Header.GenericClassesOffset, checked((int)Header.GenericClassCount), 8,
            (d, _) => (I32(d, 0), I32(d, 4))).ToArray();
        var genericTable = metadata.ReadGlobal(Header.GenericMethodTableOffset, checked((int)Header.GenericMethodTableCount), 12,
            (d, _) => (Spec: I32(d, 0), Fallback: I32(d, 4), Method: U16(d, 8)));
        var specCount = Math.Max(genericTable.Max(e => e.Spec + 1), usages.MethodSpecCount);
        var specs = metadata.ReadGlobal(Header.MethodSpecsOffset, specCount, 12,
            (d, _) => new Il2CppMethodSpec { MethodDefinitionIndex = I32(d, 0), MethodIndexIndex = I32(d, 4), ClassIndexIndex = I32(d, 8) });
        if (specs.Any(s => (uint)s.MethodDefinitionIndex >= metadata.Methods.Length || s.ClassIndexIndex < -1 || s.MethodIndexIndex < -1))
            throw new InvalidDataException("HSR generic method spec outside decoded tables.");
        var instCount = Math.Max(genericClasses.Max(c => c.Instance + 1), specs.Max(s => Math.Max(s.ClassIndexIndex, s.MethodIndexIndex) + 1));
        var instAddress = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x38);
        var instBytes = Image.ReadMappedBytes(instAddress, checked(instCount * 16));
        for (var i = 0; i < instCount; i++)
        {
            var argc = U32(instBytes, i * 16);
            if (argc is 0 or > 64)
                throw new InvalidDataException($"HSR generic instance {i} has invalid argument count {argc}.");
            var inst = new Il2CppGenericInst { TypeArgc = argc, TypeArgv = U64(instBytes, i * 16 + 8) };
            instances.Add(inst);
            instanceArguments.Add(Image.ReadMappedUWordArray(inst.TypeArgv, (int)argc).Select(TypePointerIndex).ToArray());
        }
        var maxType = metadata.Fields.Select(f => (int)f.TypeIndex)
            .Concat(metadata.Methods.Select(m => (int)m.ReturnType))
            .Concat(metadata.Definitions.Select(t => (int)t.ParentIndex))
            .Concat(metadata.Model.Params.Select(p => (int)p.TypeIndex))
            .Concat(metadata.Model.InterfaceUsageIndices.Select(i => (int)i))
            .Concat(metadata.Model.FieldDefaultValues.Select(d => (int)d.TypeIndex))
            .Concat(metadata.Model.ParameterDefaultValues.Select(d => (int)d.TypeIndex))
            .Concat(metadata.Model.FieldRefs.Select(f => (int)f.TypeIndex))
            .Concat(metadata.Model.AttributeTypeIndices)
            .Append(usages.MaxTypeIndex)
            .Concat(instanceArguments.SelectMany(a => a)).Max();
        EnsureTypes(maxType);
        metadata.ResolveEnumElementTypes(types);
        for (var i = 0; i < types.Count; i++)
        {
            var type = types[i];
            if (type.Type is Il2CppTypeEnum.IL2CPP_TYPE_PTR or Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY or Il2CppTypeEnum.IL2CPP_TYPE_BYREF)
                EnsureTypes(checked((int)type.Data.Value));
            else if (type.Type == Il2CppTypeEnum.IL2CPP_TYPE_ARRAY)
            {
                var arrayAddress = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x70) + type.Data.Value * 32;
                EnsureTypes(TypePointerIndex(Image.ReadMappedUInt64(arrayAddress)));
            }
        }
        var typeAddresses = new Dictionary<ulong, int>(types.Count);
        for (var i = 0; i < types.Count; i++)
            typeAddresses.Add(typesAddress + (ulong)i * (uint)typeStride, i);
        for (var i = 0; i < types.Count; i++)
        {
            var t = types[i];
            if (t.Type is Il2CppTypeEnum.IL2CPP_TYPE_CLASS or Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE || t.Type == Il2CppTypeEnum.IL2CPP_TYPE_OBJECT && t.Data.Value < (uint)metadata.Definitions.Length)
            {
                if (t.Data.Value >= (uint)metadata.Definitions.Length)
                    throw new InvalidDataException($"HSR type reference {i} has invalid definition {t.Data.Value}.");
                if (t.ByRef)
                    metadata.Definitions[t.Data.KlassIndex].ByRefTypeIndex = i;
                else if (metadata.Definitions[t.Data.KlassIndex].ByValTypeIndex < 0)
                    metadata.Definitions[t.Data.KlassIndex].ByValTypeIndex = i;
            }
            if (t.Type is Il2CppTypeEnum.IL2CPP_TYPE_PTR or Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY)
                t.Data.Value = typesAddress + t.Data.Value * (uint)typeStride;
            else if (t.Type == Il2CppTypeEnum.IL2CPP_TYPE_BYREF)
            {
                var element = types[checked((int)t.Data.Value)];
                element.ByRef = true;
                t = element;
            }
            else if (t.Type == Il2CppTypeEnum.IL2CPP_TYPE_ARRAY)
                t.Data.Value = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x70) + t.Data.Value * 32;
            types[i] = t;
        }
        metadata.ResolveDeclaringTypes();
        HkrpgGenerics.Read(metadata, this, specs);
        metadata.Model.Types = ImmutableCollectionsMarshal.AsImmutableArray(metadata.Definitions);
        metadata.Model.Methods = ImmutableCollectionsMarshal.AsImmutableArray(metadata.Methods);
        rawFieldOffsets = new uint[metadata.Fields.Length];
        var offsets = new uint[metadata.Fields.Length];
        var sizes = new Il2CppTypeDefinitionSizes[metadata.Definitions.Length];
        for (var i = 0; i < metadata.Definitions.Length; i++)
        {
            var group = DefinitionLayoutGroup(i);
            sizes[i] = group < 0 ? new() { NativeSize = -1 } : LayoutSizes(group);
            for (var local = 0; local < metadata.Definitions[i].FieldCount; local++)
            {
                var fi = metadata.Definitions[i].FieldIndex + local;
                rawFieldOffsets[fi] = group < 0 ? 0 : LayoutRawFieldOffset(group, local);
                // The source decoder uses the low 24 bits for every storage region.
                offsets[fi] = rawFieldOffsets[fi] & 0xFFFFFF;
            }
        }
        binary.Metadata = metadata.Model;
        binary.Modules = [];
        binary.Image.Version = metadata.Model.Version;
        binary.CodeRegistrationPointer = CodeRegistrationAddress;
        binary.MetadataRegistrationPointer = MetadataRegistrationAddress;
        binary.RegistrationFunctionPointer = RegistrationAddress;
        binary.TypeReferences = [.. types];
        binary.TypeReferenceIndicesByAddress = typeAddresses;
        binary.GenericInstances = [.. instances];
        binary.MethodSpecs = specs;
        binary.FieldOffsets = ImmutableCollectionsMarshal.AsImmutableArray(offsets);
        binary.TypeDefinitionSizes = ImmutableCollectionsMarshal.AsImmutableArray(sizes);
        var invokerCount = checked((int)(Image.ReadMappedUInt32(CodeRegistrationAddress + 0xA8) ^ 0x4695BA9Bu));
        binary.CodeRegistration = new()
        {
            InvokerPointers = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x68),
            InvokerPointersCount = (uint)invokerCount,
            CustomAttributeGenerators = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x78),
            CustomAttributeCount = metadata.AttributeRangeCount,
        };
        binary.MetadataRegistration = new() { TypesCount = types.Count, GenericInstsCount = instances.Count, MethodSpecsCount = specs.Length, GenericClassesCount = genericClasses.Length };
        binary.CustomAttributeGenerators = Image.ReadMappedUWordArray(binary.CodeRegistration.CustomAttributeGenerators, metadata.AttributeRangeCount);
        if (binary.CustomAttributeGenerators.Any(address => !IsCode(address)))
            throw new InvalidDataException("HSR custom attribute generator outside executable sections.");
        binary.MethodInvokePointers = Image.ReadMappedUWordArray(binary.CodeRegistration.InvokerPointers, invokerCount);
        BuildModules(binary, invokerCount);
        var primary = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x90);
        var secondary = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x30);
        foreach (var entry in genericTable)
        {
            var va = entry.Method != ushort.MaxValue ? Image.ReadMappedUInt64(primary + (ulong)entry.Method * 8)
                : entry.Fallback >= 0 ? Image.ReadMappedUInt64(secondary + (ulong)entry.Fallback * 8) : 0;
            if (va == 0)
                continue;
            if (!Contains(va, 1))
                throw new InvalidDataException("HSR generic method pointer outside image.");
            binary.GenericMethodPointers.TryAdd(specs[entry.Spec], va);
        }
        status?.Invoke(this, $"HSR: {types.Count} {typeStride}-byte type references, {instances.Count} generic instances, {specs.Length} method specs.");
        return binary;
    }

    private void EnsureTypes(int maxIndex)
    {
        if (maxIndex < 0 || maxIndex > 4_000_000)
            throw new InvalidDataException($"HSR type reference index out of range: {maxIndex}.");
        var start = types.Count;
        if (maxIndex < start)
            return;
        var data = Image.ReadMappedBytes(typesAddress + (ulong)start * (uint)typeStride, checked((maxIndex - start + 1) * typeStride));
        for (var i = start; i <= maxIndex; i++)
        {
            var o = (i - start) * typeStride;
            var raw = typeStride == 8 ? U32(data, o) : U64(data, o);
            var bits = U32(data, o + (typeStride == 8 ? 4 : 8));
            if (typeStride == 16 && raw >= Image.ImageBase)
            {
                if (raw >= typesAddress && (raw - typesAddress) % 16 == 0)
                    raw = (raw - typesAddress) / 16;
                else
                {
                    bits = Image.ReadMappedUInt32(raw + 8);
                    raw = Image.ReadMappedUInt64(raw);
                    if (raw >= typesAddress && (raw - typesAddress) % 16 == 0)
                        raw = (raw - typesAddress) / 16;
                }
            }
            var kind = (Il2CppTypeEnum)((bits >> 16) & 0xFF);
            if ((byte)kind is < 1 or > 0x1F)
                throw new InvalidDataException($"Invalid HSR type kind 0x{(byte)kind:X} at index {i}.");
            types.Add(new() { Data = new() { Value = unchecked((uint)raw) }, Value = bits & 0xFFFFFF | ((bits & 0x40000000) >> 1) });
        }
    }

    internal int TypePointerIndex(ulong address)
    {
        if (address < typesAddress || (address - typesAddress) % (uint)typeStride != 0)
            throw new InvalidDataException($"HSR type pointer 0x{address:X} is not aligned to the type table.");
        return checked((int)((address - typesAddress) / (uint)typeStride));
    }

    private void BuildModules(Il2CppBinary binary, int invokerCount)
    {
        // HSR uses a global method table. Module views preserve stock v24.5 lookup behavior.
        var pointersAddress = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x40);
        var pointers = Image.ReadMappedUWordArray(pointersAddress, metadata.Methods.Length);
        var invokers = metadata.ReadGlobal(Header.InvokersOffset, metadata.Methods.Length, 2, (d, _) => Index16(U16(d, 0)));
        if (invokers.Any(i => i >= invokerCount))
            throw new InvalidDataException("HSR method invoker index outside registered table.");
        foreach (var image in metadata.Model.Images)
        {
            var first = int.MaxValue;
            var last = 0;
            for (var ti = (int)image.TypeStart; ti < image.TypeStart + image.TypeCount; ti++)
            {
                var t = metadata.Definitions[ti];
                if (t.MethodCount == 0)
                    continue;
                first = Math.Min(first, t.MethodIndex);
                last = Math.Max(last, checked((int)t.MethodIndex + t.MethodCount));
            }
            if (first == int.MaxValue)
                first = last;
            // ModuleName is an opaque image-name key for this synthetic view, not a native string pointer.
            // Stock dictionaries compare module records by value, so empty/equal-sized views need identity.
            var module = new Il2CppCodeGenModule { ModuleName = unchecked((uint)image.NameIndex), MethodPointerCount = (uint)(last - first), MethodPointers = pointersAddress + (ulong)first * 8 };
            var name = metadata.Model.Strings[image.NameIndex];
            binary.Modules.Add(name, module);
            binary.ModuleMethodPointers.Add(module, pointers[first..last]);
            binary.MethodInvokerIndices.Add(module, invokers[first..last]);
            metadata.NormalizeMethodTokens(image, first, last);
        }
    }

    internal int DefinitionLayoutGroup(int definition) => I32(metadata.Global, checked(metadata.Base(Header.FieldMapOffset) + definition * 4));

    internal Il2CppTypeDefinitionSizes LayoutSizes(int group)
    {
        var o = checked(metadata.Base(Header.FieldGroupsOffset) + group * 12);
        CheckRange(metadata.Global, o, 12);
        // RVA 0x208E1AAD reads the u16 native size by layout group; 0x208E1E07 copies sizes.
        var native = Image.ReadMappedUInt16(nativeSizesAddress + checked((ulong)group * 2));
        return new()
        {
            InstanceSize = U16(metadata.Global, o + 2),
            StaticFieldsSize = U16(metadata.Global, o + 4),
            ThreadStaticFieldsSize = metadata.Global[o],
            NativeSize = native == ushort.MaxValue ? -1 : native,
        };
    }

    internal int LayoutAlignment(int group)
    {
        if (group < 0)
            return 1;
        var value = metadata.Global[checked(metadata.Base(Header.FieldGroupsOffset) + group * 12 + 6)];
        if (value is not (1 or 2 or 4 or 8 or 16))
            throw new InvalidDataException($"Invalid HSR layout alignment {value} in group {group}.");
        return value;
    }

    private uint LayoutRawFieldOffset(int group, int local)
    {
        var start = U32(metadata.Global, checked(metadata.Base(Header.FieldGroupsOffset) + group * 12 + 8));
        return U32(metadata.Global, checked(metadata.Base(Header.FieldOffsetsOffset) + (int)(start + local) * 4));
    }

    internal uint LayoutFieldOffset(int group, int local) => LayoutRawFieldOffset(group, local) & 0xFFFFFF;
    internal int FieldStorageTag(Reflection.FieldInfo field) => (int)(rawFieldOffsets[field.Index] >> 24);

    internal Reflection.TypeInfo ResolveGenericType(Reflection.TypeModel model, Il2CppType type) => HkrpgGenerics.ResolveType(metadata, this, model, type);

    private ulong[] DiscoverRegistration(int metadataLength)
    {
        foreach (var section in sections.Where(s => !s.IsBSS && s.ImageLength >= 70))
        {
            var bytes = Image.ReadBytes(section.ImageStart, section.ImageLength);
            for (var i = 0; i <= bytes.Length - 70; i++)
            {
                if (bytes[i] != 0x48 || bytes[i + 1] != 0x8D || bytes[i + 2] != 0x05)
                    continue;
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
                    continue;
                var start = Image.MapFileOffsetToVA(section.ImageStart + (uint)i);
                var targets = new ulong[6];
                targets[0] = start;
                for (var p = 0; p < 5; p++)
                    targets[p + 1] = unchecked((ulong)((long)start + p * 14 + 7 + I32(bytes, i + p * 14 + 3)));
                if (!Image.TryMapVATR(targets[5] + 0x1FC, out _) || !Image.TryMapVATR(targets[2] + 0x80, out _) || !Image.TryMapVATR(targets[1] + 0x40, out _))
                    continue;
                var span = Image.ReadMappedUInt32(targets[5] + 0x1F8) ^ 0x1608C2C8u;
                if (span <= 1_000_000 || span > metadataLength)
                    continue;
                var types = Image.ReadMappedUInt64(targets[2] + 0x80);
                var methods = Image.ReadMappedUInt64(targets[1] + 0x40);
                if (types != 0 && methods != 0 && Image.TryMapVATR(types, out _) && Image.TryMapVATR(methods, out _))
                    return targets;
            }
        }
        throw new NotSupportedException("Could not locate the HSR MORAX registration block in the original PE.");
    }

    private uint DiscoverPayloadOffset()
    {
        // Pattern: lea rcx,[rip+...]; call ...; mov rsi,rax; lea rcx,[rip+...].
        // Only accept the verified initializer sequence leading to add rsi,imm32.
        foreach (var section in sections.Where(s => !s.IsBSS && s.ImageLength >= 64))
        {
            var bytes = Image.ReadBytes(section.ImageStart, section.ImageLength);
            for (var i = 0; i <= bytes.Length - 64; i++)
            {
                if (bytes[i] != 0x48 || bytes[i + 1] != 0x8D || bytes[i + 2] != 0x0D || bytes[i + 7] != 0xE8
                    || bytes[i + 12] != 0x48 || bytes[i + 13] != 0x89 || bytes[i + 14] != 0xC6
                    || bytes[i + 15] != 0x48 || bytes[i + 16] != 0x8D || bytes[i + 17] != 0x0D)
                {
                    continue;
                }
                for (var j = i + 22; j <= i + 57; j++)
                {
                    if (bytes[j] == 0x48 && bytes[j + 1] == 0x81 && bytes[j + 2] == 0xC6)
                        return U32(bytes, j + 3);
                }
                throw new NotSupportedException("Unrecognized HSR payload initializer; could not decode add rsi,imm32.");
            }
        }
        return 0;
    }

    internal bool IsCode(ulong address) => address != 0 && sections.Any(s => s.IsExec && address >= s.VirtualStart && address <= s.VirtualEnd);
    internal bool Contains(ulong address, ulong length) => address != 0 && sections.Any(s => address >= s.VirtualStart && address <= s.VirtualEnd && length <= s.VirtualEnd - address + 1);
}
