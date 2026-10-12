using System.Collections.Immutable;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector;

internal static class HkrpgGenerics
{
    internal static void Read(HkrpgMetadata metadata, HkrpgBinary binary, ImmutableArray<Il2CppMethodSpec> specs)
    {
        var types = binary.Types;
        var instances = binary.Instances;
        var instanceArguments = binary.InstanceArguments;
        var genericClasses = binary.GenericClasses;
        var definitions = metadata.Definitions;
        var methods = metadata.Methods;
        var referencedParameters = types.Where(t => t.Type is Il2CppTypeEnum.IL2CPP_TYPE_VAR or Il2CppTypeEnum.IL2CPP_TYPE_MVAR).Select(t => checked((int)t.Data.Value)).ToArray();
        var initialParameters = metadata.ReadGlobal(metadata.Header.GenericParametersOffset, referencedParameters.Select(i => i + 1).DefaultIfEmpty().Max(), 14, HkrpgRecords.GenericParameter).ToArray();
        var containerCount = definitions.Select(t => (int)t.GenericContainerIndex + 1)
            .Concat(initialParameters.Select(p => (int)p.OwnerIndex + 1)).DefaultIfEmpty().Max();
        var containers = metadata.ReadGlobal(metadata.Header.GenericContainersOffset, containerCount, 16, HkrpgRecords.Container).ToList();
        var parameterCount = initialParameters.Length;
        for (var ci = 0; ci < containers.Count; ci++)
        {
            var c = containers[ci];
            ValidateContainer(c, ci);
            if ((uint)c.OwnerIndex >= (c.IsMethod == 1 ? methods.Length : definitions.Length))
                throw new InvalidDataException($"HSR generic container {ci} owner is outside decoded definitions.");
            if (c.IsMethod == 1)
                methods[c.OwnerIndex].GenericContainerIndex = ci;
            else if (definitions[c.OwnerIndex].GenericContainerIndex != ci)
                throw new InvalidDataException($"HSR generic type container {ci} does not match its owner definition.");
            parameterCount = Math.Max(parameterCount, checked((int)c.GenericParameterStart + c.TypeArgc));
        }
        for (var ti = 0; ti < definitions.Length; ti++)
        {
            var ci = (int)definitions[ti].GenericContainerIndex;
            if (ci >= 0)
            {
                var c = containers[ci];
                ValidateContainer(c, ci);
                if (c.OwnerIndex != ti || c.IsMethod != 0)
                    throw new InvalidDataException($"HSR generic container {ci} has an inconsistent type owner.");
                containers[ci] = c;
                parameterCount = Math.Max(parameterCount, checked((int)c.GenericParameterStart + c.TypeArgc));
            }
        }
        var cache = new Dictionary<int, int>();
        int FindMethodContainer(int typeIndex, HashSet<int> visiting)
        {
            if (cache.TryGetValue(typeIndex, out var cached))
                return cached;
            if (!visiting.Add(typeIndex))
                return -1;
            var type = types[typeIndex];
            var result = -1;
            if (type.Type == Il2CppTypeEnum.IL2CPP_TYPE_MVAR)
                result = initialParameters[checked((int)type.Data.Value)].OwnerIndex;
            else if (type.Type is Il2CppTypeEnum.IL2CPP_TYPE_PTR or Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY)
                result = FindMethodContainer(binary.TypePointerIndex(type.Data.Value), visiting);
            else if (type.Type == Il2CppTypeEnum.IL2CPP_TYPE_ARRAY)
                result = FindMethodContainer(binary.TypePointerIndex(binary.Image.ReadMappedUInt64(type.Data.Value)), visiting);
            else if (type.Type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
            {
                var instance = genericClasses[checked((int)type.Data.Value)].Instance;
                if (instance >= 0)
                    foreach (var argument in instanceArguments[instance])
                    {
                        result = FindMethodContainer(argument, visiting);
                        if (result >= 0)
                            break;
                    }
            }
            visiting.Remove(typeIndex);
            cache[typeIndex] = result;
            return result;
        }
        for (var mi = 0; mi < methods.Length; mi++)
        {
            var m = methods[mi];
            var owner = FindMethodContainer(m.ReturnType, []);
            for (var pi = 0; owner < 0 && pi < m.ParameterCount; pi++)
                owner = FindMethodContainer(metadata.Model.Params[m.ParameterStart + pi].TypeIndex, []);
            if (owner < 0)
                continue;
            var c = containers[owner];
            ValidateContainer(c, owner);
            if (c.OwnerIndex >= 0 && (c.IsMethod != 1 || c.OwnerIndex != mi))
                throw new InvalidDataException($"HSR generic container {owner} has conflicting owners.");
            c.OwnerIndex = mi;
            c.IsMethod = 1;
            containers[owner] = c;
            methods[mi].GenericContainerIndex = owner;
            parameterCount = Math.Max(parameterCount, checked((int)c.GenericParameterStart + c.TypeArgc));
        }
        var parameters = metadata.ReadGlobal(metadata.Header.GenericParametersOffset, parameterCount, 14, HkrpgRecords.GenericParameter).ToList();
        // Some generic methods do not mention MVAR in their signature. Recover arity from specs.
        foreach (var spec in specs.Where(s => s.MethodIndexIndex >= 0))
        {
            var methodIndex = spec.MethodDefinitionIndex;
            var argc = checked((int)instances[spec.MethodIndexIndex].TypeArgc);
            if (methods[methodIndex].GenericContainerIndex >= 0)
            {
                if (containers[methods[methodIndex].GenericContainerIndex].TypeArgc != argc)
                    throw new InvalidDataException($"HSR method {methodIndex} has inconsistent generic arity.");
                continue;
            }
            var ci = containers.Count;
            containers.Add(new() { OwnerIndex = methodIndex, IsMethod = 1, TypeArgc = argc, GenericParameterStart = parameters.Count });
            methods[methodIndex].GenericContainerIndex = ci;
            for (var p = 0; p < argc; p++)
            {
                var name = -1000 - parameters.Count;
                metadata.Model.Strings.Add(name, "T" + p);
                parameters.Add(new() { OwnerIndex = ci, Num = (ushort)p, NameIndex = name });
            }
        }
        for (var ci = 0; ci < containers.Count; ci++)
        {
            var c = containers[ci];
            if (c.OwnerIndex < 0)
                continue;
            for (var pi = 0; pi < c.TypeArgc; pi++)
            {
                var index = c.GenericParameterStart + pi;
                var parameter = parameters[index];
                if (parameter.OwnerIndex != ci || parameter.Num != pi)
                    throw new InvalidDataException($"HSR generic parameter {index} has inconsistent owner/position.");
                metadata.Intern(parameter.NameIndex);
            }
        }
        metadata.Model.GenericContainers = [.. containers];
        metadata.Model.GenericParameters = [.. parameters];
        metadata.Model.GenericConstraintIndices = [];

        static void ValidateContainer(Il2CppGenericContainer c, int index)
        {
            if (c.TypeArgc is < 1 or > 64 || c.GenericParameterStart < 0)
                throw new InvalidDataException($"Invalid HSR generic container {index}.");
        }
    }

    internal static Reflection.TypeInfo ResolveType(HkrpgMetadata metadata, HkrpgBinary binary, Reflection.TypeModel model, Il2CppType type)
    {
        var genericClasses = binary.GenericClasses;
        var instanceArguments = binary.InstanceArguments;
        var types = binary.Types;
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
                    ? $":owner={metadata.Model.GenericParameters[t.Data.GenericParameterIndex].OwnerIndex}:definition={metadata.Model.GenericContainers[metadata.Model.GenericParameters[t.Data.GenericParameterIndex].OwnerIndex].OwnerIndex}"
                    : "";
                return $"{i}:{t.Type}:{t.Data.Value}{owner}";
            }));
            throw new InvalidDataException($"HSR generic class {type.Data.Value}, definition {pair.Definition}, instance {pair.Instance}, args [{arguments}], parameter count {metadata.Model.GenericParameters.Length}: {ex.Message}", ex);
        }
    }
}
