using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector;

internal sealed partial class HkrpgMorax
{
    private readonly List<Il2CppType> types = [];
    private readonly List<Il2CppGenericInst> instances = [];
    private readonly List<int[]> instanceArguments = [];
    private (int Definition, int Instance)[] genericClasses;
    private ulong typesAddress;
    private int typeStride;
    private uint[] rawFieldOffsets;
    public override int VTableSlotSize => 16;

    private void ReadBinary(Il2CppBinary binary)
    {
        ReadUsages();
        status?.Invoke(this, "Decoding HSR type references and generic instances");
        typesAddress = Image.ReadMappedUInt64(MetadataRegistrationAddress + 0x80);
        var probe = Image.ReadMappedBytes(typesAddress, 256);
        var votes8 = Enumerable.Range(0, 16).Count(i => probe[i * 8 + 6] is >= 1 and <= 0x1F);
        var votes16 = Enumerable.Range(0, 16).Count(i => probe[i * 16 + 10] is >= 1 and <= 0x1F);
        typeStride = votes16 >= votes8 ? 16 : 8;
        genericClasses = Records(startup, checked((int)header.GenericClassesOffset), checked((int)header.GenericClassCount), 8,
            (d, _) => (I32(d, 0), I32(d, 4))).ToArray();
        var genericTable = Records(global, Base(header.GenericMethodTableOffset), checked((int)header.GenericMethodTableCount), 12,
            (d, _) => (Spec: I32(d, 0), Fallback: I32(d, 4), Method: U16(d, 8)));
        var specCount = Math.Max(genericTable.Max(e => e.Spec + 1), usagePairs.Where(p => p.Kind == 6).Select(p => p.Source + 1).DefaultIfEmpty().Max());
        var specs = Records(global, Base(header.MethodSpecsOffset), specCount, 12,
            (d, _) => new Il2CppMethodSpec { MethodDefinitionIndex = I32(d, 0), MethodIndexIndex = I32(d, 4), ClassIndexIndex = I32(d, 8) });
        if (specs.Any(s => (uint)s.MethodDefinitionIndex >= methods.Length || s.ClassIndexIndex < -1 || s.MethodIndexIndex < -1))
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
        var maxType = fields.Select(f => (int)f.TypeIndex)
            .Concat(methods.Select(m => (int)m.ReturnType))
            .Concat(definitions.Select(t => (int)t.ParentIndex))
            .Concat(Metadata.Params.Select(p => (int)p.TypeIndex))
            .Concat(Metadata.InterfaceUsageIndices.Select(i => (int)i))
            .Concat(Metadata.FieldDefaultValues.Select(d => (int)d.TypeIndex))
            .Concat(Metadata.ParameterDefaultValues.Select(d => (int)d.TypeIndex))
            .Concat(Metadata.FieldRefs.Select(f => (int)f.TypeIndex))
            .Concat(usagePairs.Where(p => p.Kind is 1 or 7).Select(p => p.Source))
            .Concat(instanceArguments.SelectMany(a => a)).Max();
        EnsureTypes(maxType);
        ResolveEnumElementTypes();
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
            if (t.Type is Il2CppTypeEnum.IL2CPP_TYPE_CLASS or Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE || t.Type == Il2CppTypeEnum.IL2CPP_TYPE_OBJECT && t.Data.Value < (uint)definitions.Length)
            {
                if (t.Data.Value >= (uint)definitions.Length)
                    throw new InvalidDataException($"HSR type reference {i} has invalid definition {t.Data.Value}.");
                if (t.ByRef)
                    definitions[t.Data.KlassIndex].ByRefTypeIndex = i;
                else if (definitions[t.Data.KlassIndex].ByValTypeIndex < 0)
                    definitions[t.Data.KlassIndex].ByValTypeIndex = i;
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
        foreach (var (child, parent) in declaringDefinitions)
        {
            if (definitions[parent].ByValTypeIndex < 0)
                throw new InvalidDataException($"HSR declaring type {parent} has no type reference.");
            definitions[child].DeclaringTypeIndex = definitions[parent].ByValTypeIndex;
        }
        ReadGenerics(specs);
        Metadata.Types = ImmutableCollectionsMarshal.AsImmutableArray(definitions);
        Metadata.Methods = ImmutableCollectionsMarshal.AsImmutableArray(methods);
        rawFieldOffsets = new uint[fields.Length];
        var offsets = new uint[fields.Length];
        var sizes = new Il2CppTypeDefinitionSizes[definitions.Length];
        for (var i = 0; i < definitions.Length; i++)
        {
            var group = DefinitionLayoutGroup(i);
            sizes[i] = group < 0 ? new() { NativeSize = -1 } : LayoutSizes(group);
            for (var local = 0; local < definitions[i].FieldCount; local++)
            {
                var fi = definitions[i].FieldIndex + local;
                rawFieldOffsets[fi] = group < 0 ? 0 : LayoutRawFieldOffset(group, local);
                // The source decoder uses the low 24 bits for every storage region.
                offsets[fi] = rawFieldOffsets[fi] & 0xFFFFFF;
            }
        }
        binary.Metadata = Metadata;
        binary.Modules = [];
        binary.Image.Version = Metadata.Version;
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
        };
        binary.MetadataRegistration = new() { TypesCount = types.Count, GenericInstsCount = instances.Count, MethodSpecsCount = specs.Length, GenericClassesCount = genericClasses.Length };
        binary.CustomAttributeGenerators = [];
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

    private int TypePointerIndex(ulong address)
    {
        if (address < typesAddress || (address - typesAddress) % (uint)typeStride != 0)
            throw new InvalidDataException($"HSR type pointer 0x{address:X} is not aligned to the type table.");
        return checked((int)((address - typesAddress) / (uint)typeStride));
    }

    private void BuildModules(Il2CppBinary binary, int invokerCount)
    {
        // HSR uses a global method table. Module views preserve stock v24.5 lookup behavior.
        var pointersAddress = Image.ReadMappedUInt64(CodeRegistrationAddress + 0x40);
        var pointers = Image.ReadMappedUWordArray(pointersAddress, methods.Length);
        var invokers = Records(global, Base(header.InvokersOffset), methods.Length, 2, (d, _) => Index16(U16(d, 0)));
        if (invokers.Any(i => i >= invokerCount))
            throw new InvalidDataException("HSR method invoker index outside registered table.");
        foreach (var image in Metadata.Images)
        {
            var first = int.MaxValue;
            var last = 0;
            for (var ti = (int)image.TypeStart; ti < image.TypeStart + image.TypeCount; ti++)
            {
                var t = definitions[ti];
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
            var name = Metadata.Strings[image.NameIndex];
            binary.Modules.Add(name, module);
            binary.ModuleMethodPointers.Add(module, pointers[first..last]);
            binary.MethodInvokerIndices.Add(module, invokers[first..last]);
            for (var mi = first; mi < last; mi++)
                methods[mi].Token = 0x06000000u | (uint)(mi - first + 1);
        }
    }

    internal int DefinitionLayoutGroup(int definition) => I32(global, checked(Base(header.FieldMapOffset) + definition * 4));

    public override Il2CppTypeDefinitionSizes LayoutSizes(int group)
    {
        var o = checked(Base(header.FieldGroupsOffset) + group * 12);
        return new() { InstanceSize = U16(global, o + 2), StaticFieldsSize = U16(global, o), ThreadStaticFieldsSize = U16(global, o + 4), NativeSize = -1 };
    }

    internal int LayoutAlignment(int group)
    {
        if (group < 0)
            return 1;
        var value = global[checked(Base(header.FieldGroupsOffset) + group * 12 + 6)];
        if (value is not (1 or 2 or 4 or 8 or 16))
            throw new InvalidDataException($"Invalid HSR layout alignment {value} in group {group}.");
        return value;
    }

    private uint LayoutRawFieldOffset(int group, int local)
    {
        var start = U32(global, checked(Base(header.FieldGroupsOffset) + group * 12 + 8));
        return U32(global, checked(Base(header.FieldOffsetsOffset) + (int)(start + local) * 4));
    }

    public override uint LayoutFieldOffset(int group, int local) => LayoutRawFieldOffset(group, local) & 0xFFFFFF;
    public override int FieldStorageTag(Reflection.FieldInfo field) => (int)(rawFieldOffsets[field.Index] >> 24);
    public override string StorageBase(uint tag) => tag == 0 ? "klass->static_fields" : $"MORAX storage region {tag}";

    public override Reflection.TypeInfo ResolveGenericType(Reflection.TypeModel model, Il2CppType type)
    {
        var pair = genericClasses[checked((int)type.Data.Value)];
        if (pair.Definition < 0 || pair.Instance < 0)
            return null;
        try
        {
            return model.TypesByDefinitionIndex[pair.Definition].MakeGenericType(model.ResolveGenericArguments(model.Package.GenericInstances[pair.Instance]));
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or KeyNotFoundException)
        {
            var arguments = string.Join(", ", instanceArguments[pair.Instance].Select(i =>
            {
                var t = types[i];
                var owner = t.Type is Il2CppTypeEnum.IL2CPP_TYPE_VAR or Il2CppTypeEnum.IL2CPP_TYPE_MVAR
                    ? $":owner={Metadata.GenericParameters[t.Data.GenericParameterIndex].OwnerIndex}:definition={Metadata.GenericContainers[Metadata.GenericParameters[t.Data.GenericParameterIndex].OwnerIndex].OwnerIndex}"
                    : "";
                return $"{i}:{t.Type}:{t.Data.Value}{owner}";
            }));
            throw new InvalidDataException($"HSR generic class {type.Data.Value}, definition {pair.Definition}, instance {pair.Instance}, args [{arguments}], parameter count {Metadata.GenericParameters.Length}: {ex.Message}", ex);
        }
    }
}
