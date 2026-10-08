using Cecil = Mono.Cecil;

namespace Il2CppInspector.Tests.Plugin.ZZZ.Outputs
{
    internal static class ZzzAssemblyWriterTests
    {
        internal static void VerifyAssemblyResolution(string directory)
        {
            using var resolver = new RegisteredAssemblyResolver();
            var parameters = new Cecil.ReaderParameters { AssemblyResolver = resolver };
            var assemblies = new Dictionary<string, Cecil.AssemblyDefinition>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(directory, "*.dll"))
            {
                var assembly = Cecil.AssemblyDefinition.ReadAssembly(file, parameters);
                resolver.Register(assembly);
                assemblies.Add(assembly.Name.Name, assembly);
            }

            var references = 0;
            foreach (var assembly in assemblies.Values)
            {
                foreach (var reference in assembly.MainModule.AssemblyReferences)
                {
                    if (!assemblies.TryGetValue(reference.Name, out var target) || reference.FullName != target.Name.FullName)
                        throw new InvalidOperationException($"Assembly reference identity mismatch in {assembly.Name.Name}: {reference.FullName}; target: {target?.Name.FullName}");
                    if (!ReferenceEquals(resolver.Resolve(reference), target))
                        throw new InvalidOperationException($"Assembly reference bypassed the preloaded DLL cache: {reference.FullName}");
                    references++;
                }
            }
            Check(references > 0, $"All {references} DLL references match and resolve to preloaded assembly identities without search directories");

            var behaviour = assemblies["Assembly-CSharp"].MainModule.GetTypeReferences().First(t => t.FullName == "UnityEngine.MonoBehaviour");
            var resolved = behaviour.Resolve();
            Check(
                resolved?.FullName == behaviour.FullName && ReferenceEquals(resolved.Module.Assembly, assemblies["UnityEngine.CoreModule"]),
                "Mono.Cecil resolves UnityEngine.MonoBehaviour through the preloaded CoreModule DLL"
            );
        }

        private sealed class RegisteredAssemblyResolver : Cecil.DefaultAssemblyResolver
        {
            internal void Register(Cecil.AssemblyDefinition assembly) => RegisterAssembly(assembly);
        }

        internal static void VerifyNameTranslation(ZzzTestContext context, string[] args)
        {
            var model = context.Model;
            var vector = context.Vector;
            model.ApplyNameTranslation([
                "#ReverseOrder",
                "#Classes",
                "Vector3\u21E8Audit.VectorMoved",
                "#Methods",
                "Dot\u21E8Audit.VectorMoved::TranslatedDot()",
                "#Fields",
                "x\u21E8Audit.VectorMoved::axisX",
                "#Properties",
                "normalized\u21E8Audit.VectorMoved::normalizedAudit()",
                "#Parameters",
                "lhs\u21E8left",
            ]);
            new AssemblyShims(model).Write(Path.Combine(args[1], "DummyDll"));
            using var translated = ModuleDefMD.Load(Path.Combine(args[1], "DummyDll", vector.Assembly.ShortName));
            var moved = translated.GetTypes().Single(t => t.FullName == "Audit.VectorMoved");
            Check(
                moved.Fields.Any(f => f.Name == "axisX")
                    && moved.Properties.Any(p => p.Name == "normalizedAudit")
                    && moved.Methods.Any(m => m.Name == "TranslatedDot" && m.Parameters.Any(p => p.Name == "left")),
                "ZZZ DLL definitions retain translated type namespaces, fields, properties, methods and parameters"
            );
        }

        internal static void VerifySuppressed(ZzzTestContext context, string output)
        {
            var model = context.Model;
            new AssemblyShims(model) { SuppressMetadata = true }.Write(Path.Combine(output, "SuppressedDll"));
            VerifyAssemblyResolution(Path.Combine(output, "SuppressedDll"));
            using var suppressed = ModuleDefMD.Load(Path.Combine(output, "SuppressedDll", "mscorlib.dll"));
            Check(
                suppressed.GetTypes().SelectMany(t => t.CustomAttributes.Concat(t.Methods.SelectMany(m => m.CustomAttributes))).Any(a => a.AttributeType.FullName == "System.FlagsAttribute")
                    && !suppressed.GetAssemblyRefs().Any(a => a.Name == "Il2CppInspector"),
                "Suppressed DLL output preserves safe real attributes while removing informational helper dependencies"
            );
        }

        internal static void VerifyAssemblies(ZzzTestContext context, string output)
        {
            VerifyAssemblyResolution(Path.Combine(output, "DummyDll"));
            var input = context.Input;
            var model = context.Model;
            var rawOffsets = context.RawOffsets;
            long methods = 0,
                fields = 0,
                properties = 0,
                events = 0,
                nestedTypes = 0;
            long addressAttributes = 0;
            var rvaFields = 0;
            var scalarRvaFields = 0;
            var staticAttributes = 0;
            var directAttributeCount = 0;
            var informationalAttributeCount = 0;
            var checkedFieldDefaults = 0;
            var checkedParameterDefaults = 0;
            var checkedPacking = 0;
            foreach (var image in input.Images)
            {
                using var dll = ModuleDefMD.Load(Path.Combine(output, "DummyDll", input.Strings[image.NameIndex]));
                var types = dll.GetTypes().ToArray();
                var attributeOwners = new IHasCustomAttribute[] { dll.Assembly }
                    .Concat(types.Cast<IHasCustomAttribute>())
                    .Concat(types.SelectMany(t => t.Fields))
                    .Concat(types.SelectMany(t => t.Methods))
                    .Concat(types.SelectMany(t => t.Properties))
                    .Concat(types.SelectMany(t => t.Events))
                    .Concat(types.SelectMany(t => t.Methods).SelectMany(m => m.ParamDefs));
                foreach (var owner in attributeOwners)
                {
                    var tokenAttribute = owner.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "TokenAttribute");
                    if (tokenAttribute == null)
                        continue;
                    var token = Convert.ToUInt32(tokenAttribute.NamedArguments.Single(a => a.Name == "Token").Argument.Value.ToString()[2..], 16);
                    if (!input.AttributeIndicesByToken.TryGetValue(image.CustomAttributeStart, out var tokenMap) || !tokenMap.TryGetValue(token, out var rangeIndex))
                        continue;
                    var range = input.AttributeTypeRanges[rangeIndex];
                    var informational = owner
                        .CustomAttributes.Where(a => a.AttributeType.Name == "AttributeAttribute")
                        .Select(a => a.NamedArguments.Single(n => n.Name == "Name").Argument.Value.ToString())
                        .ToList();
                    for (var a = 0; a < range.Count; a++)
                    {
                        var attribute = model.TypesByReferenceIndex[input.AttributeTypeIndices[range.Start + a]];
                        var safe =
                            attribute.DeclaredFields.Count == 0 && attribute.DeclaredProperties.Count == 0 && attribute.DeclaredConstructors.Any(c => !c.IsStatic && c.DeclaredParameters.Count == 0);
                        if (safe)
                        {
                            var restored = owner.CustomAttributes.FirstOrDefault(c => c.AttributeType.FullName.Replace('/', '+') == attribute.FullName);
                            if (restored == null || restored.ConstructorArguments.Count != 0 || restored.NamedArguments.Count != 0)
                                throw new InvalidOperationException(
                                    $"Safe real attribute missing: {attribute.FullName}, base={attribute.BaseType?.FullName}, actual="
                                        + string.Join(",", owner.CustomAttributes.Select(c => c.AttributeType.FullName))
                                );
                            directAttributeCount++;
                        }
                        else
                        {
                            if (!informational.Remove(attribute.Name))
                                throw new InvalidOperationException($"Informational attribute missing: {attribute.FullName}");
                            informationalAttributeCount++;
                        }
                    }
                    if (informational.Count != 0)
                        throw new InvalidOperationException("Unexpected informational attribute after restoring real attributes");
                }
                var originalTypes = types
                    .Select(t => (Type: t, Token: t.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "TokenAttribute")))
                    .Where(t => t.Token != null)
                    .ToDictionary(t => Convert.ToUInt32(t.Token.NamedArguments.Single(a => a.Name == "Token").Argument.Value.ToString()[2..], 16), t => t.Type);
                if ((uint)dll.Assembly.HashAlgorithm != (uint)input.Assemblies[image.AssemblyIndex].Aname.HashAlg)
                    throw new InvalidOperationException("Exported assembly hash algorithm differs from metadata");
                var originalAssembly = input.Assemblies[image.AssemblyIndex].Aname;
                if (dll.Assembly.Version != new Version(originalAssembly.Major, originalAssembly.Minor, originalAssembly.Build, originalAssembly.Revision))
                    throw new InvalidOperationException("Exported assembly version differs from decoded runtime identity");
                var flags = dll.Assembly.CustomAttributes.Single(a => a.AttributeType.Name == "AssemblyFlagsAttribute");
                if (flags.NamedArguments.Single(a => a.Name == "Flags").Argument.Value.ToString() != $"0x{(uint)originalAssembly.Flags:X}" || dll.Assembly.HasPublicKey)
                    throw new InvalidOperationException("Shim must preserve recovered flags informationally without claiming a missing strong-name key");
                for (var ti = image.TypeStart; ti < image.TypeStart + image.TypeCount; ti++)
                {
                    var td = input.TypeDefinitions[ti];
                    if (input.Strings[td.NameIndex] == "<Module>")
                        continue;
                    var exported = originalTypes[td.Token];
                    if (exported.Interfaces.Count != td.InterfacesCount)
                        throw new InvalidOperationException("DLL interface list differs from metadata");
                    if (exported.ClassLayout != null)
                    {
                        var pack = (int)td.Bitfield.PackingSize;
                        var expectedPack = td.Bitfield.DefaultPackingSize || pack == 0 ? 0 : 1 << (pack - 1);
                        if (exported.ClassLayout.PackingSize != expectedPack || exported.ClassLayout.ClassSize != Math.Max(0, (long)input.TypeDefinitionSizes[ti].InstanceSize - 16))
                            throw new InvalidOperationException("DLL managed layout or packing differs from metadata");
                        checkedPacking++;
                    }
                    var methodsByToken = exported.Methods.ToDictionary(m =>
                        Convert.ToUInt32(m.CustomAttributes.Single(a => a.AttributeType.Name == "TokenAttribute").NamedArguments.Single(a => a.Name == "Token").Argument.Value.ToString()[2..], 16)
                    );
                    for (var methodIndex = 0; methodIndex < td.MethodCount; methodIndex++)
                    {
                        var rawMethod = input.Methods[td.MethodIndex + methodIndex];
                        var method = methodsByToken[rawMethod.Token];
                        for (var p = 0; p < rawMethod.ParameterCount; p++)
                        {
                            var parameterIndex = rawMethod.ParameterStart + p;
                            var parameter = method.ParamDefs.Single(d => d.Sequence == p + 1);
                            if (input.ParameterDefaultValue.TryGetValue(parameterIndex, out var expected) && parameter.HasDefault)
                            {
                                if (!parameter.HasConstant || !Equals(parameter.Constant.Value, expected.Item2))
                                    throw new InvalidOperationException("DLL parameter default differs from metadata");
                                checkedParameterDefaults++;
                                if (
                                    expected.Item1 != 0
                                    && Convert.ToUInt64(
                                        parameter
                                            .CustomAttributes.Single(a => a.AttributeType.Name == "MetadataOffsetAttribute")
                                            .NamedArguments.Single(a => a.Name == "Offset")
                                            .Argument.Value.ToString()[2..],
                                        16
                                    ) != expected.Item1
                                )
                                    throw new InvalidOperationException("DLL parameter metadata address differs from input");
                            }
                        }
                    }
                    for (var local = 0; local < td.FieldCount; local++)
                    {
                        var index = td.FieldIndex + local;
                        var original = input.Fields[index];
                        var field = exported.Fields[local];
                        var storage = input.TypeReferences[original.TypeIndex];
                        if (field.Name != input.Strings[original.NameIndex])
                            throw new InvalidOperationException("Exported field order differs");
                        if (input.FieldDefaultValue.TryGetValue(index, out var expectedDefault) && field.HasDefault)
                        {
                            if (!field.HasConstant || !Equals(field.Constant.Value, expectedDefault.Item2))
                                throw new InvalidOperationException("DLL field default differs from metadata");
                            checkedFieldDefaults++;
                        }
                        if (field.IsStatic && !field.IsLiteral)
                        {
                            var attribute = field.CustomAttributes.Single(a => a.AttributeType.Name == "StaticFieldOffsetAttribute");
                            var offset = attribute.NamedArguments.Single(a => a.Name == "Offset").Argument.Value.ToString();
                            if (Convert.ToUInt32(offset[2..], 16) != (uint)(input.FieldOffsets[index] & 0x7FFFFFFF))
                                throw new InvalidOperationException("DLL static offset differs from model");
                            if (attribute.NamedArguments.Single(a => a.Name == "StorageTag").Argument.Value.ToString() != (rawOffsets[index] >> 24).ToString())
                                throw new InvalidOperationException("DLL static storage region differs from runtime tag");
                            staticAttributes++;
                        }
                        if (!field.HasFieldRVA)
                            continue;
                        rvaFields++;
                        var size = storage.Type switch
                        {
                            Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE => checked((int)input.TypeDefinitionSizes[storage.Data.KlassIndex].InstanceSize - 16),
                            Il2CppTypeEnum.IL2CPP_TYPE_I8 => 8,
                            Il2CppTypeEnum.IL2CPP_TYPE_I4 => 4,
                            _ => throw new InvalidOperationException("Unexpected FieldRVA storage type"),
                        };
                        if (storage.Type != Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
                            scalarRvaFields++;
                        var expected = input.Metadata.ReadBytes((long)input.FieldDefaultValue[index].Item1, size);
                        if (field.RVA == 0 || !field.InitialValue.SequenceEqual(expected))
                            throw new InvalidOperationException($"DLL FieldRVA payload differs from metadata: {field.FullName}, expected size {size}, actual size {field.InitialValue?.Length}");
                        if (
                            field.CustomAttributes.Single(a => a.AttributeType.Name == "MetadataPreviewAttribute").NamedArguments.Single(a => a.Name == "Data").Argument.Value.ToString()
                                != Convert.ToHexString(expected)
                            || Convert.ToUInt64(
                                field.CustomAttributes.Single(a => a.AttributeType.Name == "MetadataOffsetAttribute").NamedArguments.Single(a => a.Name == "Offset").Argument.Value.ToString()[2..],
                                16
                            ) != input.FieldDefaultValue[index].Item1
                        )
                            throw new InvalidOperationException("DLL FieldRVA preview/address attributes differ from metadata");
                    }
                }
                methods += types.Sum(t => t.Methods.Count);
                addressAttributes += types.SelectMany(t => t.Methods).Count(m => m.CustomAttributes.Any(a => a.AttributeType.Name.String is "AddressAttribute" or "GenericInstAddressAttribute"));
                fields += types.Sum(t => t.Fields.Count);
                properties += types.Sum(t => t.Properties.Count);
                events += types.Sum(t => t.Events.Count);
                nestedTypes += types.Count(t => t.IsNested);
            }
            Check(
                methods == input.Methods.Length
                    && fields == input.Fields.Length
                    && properties == model.TypesByDefinitionIndex.Where(t => t != null).Sum(t => t.DeclaredProperties.Count)
                    && events == input.Events.Length,
                $"All 170 exported DLLs reload with complete member counts: methods={methods}/{input.Methods.Length}, fields={fields}/{input.Fields.Length}, properties={properties}/{input.Properties.Length}, events={events}/{input.Events.Length}"
            );
            Pass("All 170 DLL assembly versions and hash algorithms match metadata");
            Check(
                directAttributeCount > 1000 && informationalAttributeCount > 0,
                $"All metadata attributes retain safe real attributes ({directAttributeCount}) or informational fallbacks ({informationalAttributeCount})"
            );
            Check(
                checkedFieldDefaults > 50000 && checkedParameterDefaults > 10000 && checkedPacking > 1000,
                $"DLL defaults and layouts match metadata: fields={checkedFieldDefaults}, parameters={checkedParameterDefaults}, layouts={checkedPacking}"
            );
            Check(
                rvaFields == 1020 && scalarRvaFields == 24 && staticAttributes > 0,
                "All 1,020 DLL FieldRVA payloads match metadata, including 24 scalars; static attributes retain runtime buffer offsets and tags"
            );
            Check(nestedTypes == 28346, "DLL nesting matches all 28,346 metadata children");
            Check(addressAttributes == 817194, $"817,194 DLL methods have ordinary or generic-instantiation address metadata (actual {addressAttributes})");
            using var unity = ModuleDefMD.Load(Path.Combine(output, "DummyDll", "UnityEngine.CoreModule.dll"));
            var v = unity.GetTypes().Single(t => t.FullName == "UnityEngine.Vector3");
            Check(v.Fields.Single(f => f.Name == "kEpsilon").Constant.Value is float epsilon && epsilon > 0 && epsilon < 0.001f, "Exported Vector3 constant is a typed float");
            Check(v.Methods.Any(m => m.CustomAttributes.Any(a => a.AttributeType.Name == "AddressAttribute")), "DLL methods retain address attributes");
            using var core = ModuleDefMD.Load(Path.Combine(output, "DummyDll", "mscorlib.dll"));
            var day = core.GetTypes().Single(t => t.FullName == "System.DayOfWeek");
            Check(day.IsEnum && day.BaseType.FullName == "System.Enum", "Exported enums derive from System.Enum rather than their MORAX storage type");
        }
    }
}
