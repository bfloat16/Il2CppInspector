using System.Text;
using Il2CppInspector.Cpp;
using Il2CppInspector.Outputs;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model
{
    // The public analysis model is materialized on request; file exports use the streaming writers.
    internal sealed class ZzzAnalysisModel(AppModel model) : Plugins.IGameAnalysisModel
    {
        private ZzzMorax Adapter => (ZzzMorax)Package.Metadata.GameAdapter;

        private ZzzNativeModel Native => (ZzzNativeModel)model.GameNativeModel;
        private Il2CppInspector Package => model.Package;
        private TypeModel Reflection => model.TypeModel;
        private CppTypeCollection Cpp => model.RuntimeCppTypes;
        private bool methodsBuilt,
            typesBuilt,
            orderBuilt;
        private string group = "unused_concrete_types";

        public void BuildMethods()
        {
            if (methodsBuilt)
            {
                return;
            }

            for (var index = 0; index < Package.Methods.Length; index++)
            {
                if (Reflection.MethodsByDefinitionIndex[index].VirtualAddress.HasValue)
                {
                    AddMethod(Reflection.MethodsByDefinitionIndex[index], index, -1, "types_from_methods");
                }
            }

            for (var index = 0; index < Package.MethodSpecs.Length; index++)
            {
                var spec = Package.MethodSpecs[index];
                if (Package.GenericMethodPointers.ContainsKey(spec))
                {
                    AddMethod(Reflection.GetGenericMethod(spec), spec.MethodDefinitionIndex, index, "types_from_generic_methods");
                }
            }
            foreach (var usage in Package.MetadataUsages.Where(u => u.Type is MetadataUsageType.MethodDef or MetadataUsageType.MethodRef))
            {
                var method = Reflection.GetMetadataUsageMethod(usage);
                var spec = usage.Type == MetadataUsageType.MethodRef ? usage.SourceIndex : -1;
                var definition = spec < 0 ? usage.SourceIndex : Package.MethodSpecs[spec].MethodDefinitionIndex;
                AddMethod(method, definition, spec, "types_from_usages").MethodInfoPtrAddress = usage.VirtualAddress;
            }
            methodsBuilt = true;

            AppMethod AddMethod(MethodBase method, int definition, int spec, string group)
            {
                if (model.AnalysisMethods.ContainsKey(method))
                {
                    return model.AnalysisMethods[method];
                }

                this.group = group;
                var parameters = new List<(string Name, CppType Type)>();
                var names = new HashSet<string> { "__this", "method" };
                if (!method.IsStatic)
                {
                    parameters.Add(("__this", AsCType(method.DeclaringType.IsValueType ? method.DeclaringType.MakeByRefType() : method.DeclaringType)));
                }

                for (var i = 0; i < method.DeclaredParameters.Count; i++)
                {
                    var parameter = method.DeclaredParameters[i];
                    var name = ZzzHeaderWriter.FieldName(parameter.Name);
                    if (name.Length == 0)
                    {
                        name = $"arg{i}";
                    }

                    while (!names.Add(name))
                    {
                        name += "_" + i;
                    }

                    parameters.Add((name, AsCType(parameter.ParameterType)));
                }
                parameters.Add(("method", Cpp.GetType("MethodInfo *")));
                var pointer = new CppFnPtrType(64, method is MethodInfo info ? AsCType(info.ReturnType) : Cpp.GetType("void"), parameters)
                {
                    Name = Native.MethodName(definition, spec),
                    Group = group,
                };
                var result = new AppMethod(method, pointer) { Group = group };
                model.AnalysisMethods.Add(method, pointer, result);
                return result;
            }
        }

        public void BuildTypes()
        {
            BuildMethods();
            RegisterTypes();
        }

        public IEnumerable<CppType> EnumerateNativeTypes()
        {
            RegisterTypes();
            return Cpp.Types.Values.ToArray();
        }

        public IEnumerable<NativeMethod> EnumerateNativeMethods()
        {
            for (var index = 0; index < Package.Methods.Length; index++)
                if (Reflection.MethodsByDefinitionIndex[index].VirtualAddress is { } address)
                    yield return NativeMethod(index, -1, address.Start);
            for (var index = 0; index < Package.MethodSpecs.Length; index++)
            {
                var spec = Package.MethodSpecs[index];
                if (Package.GenericMethodPointers.TryGetValue(spec, out var address))
                    yield return NativeMethod(spec.MethodDefinitionIndex, index, address);
            }
        }

        private NativeMethod NativeMethod(int definition, int specIndex, ulong address)
        {
            var d = Package.Methods[definition];
            var method = Reflection.MethodsByDefinitionIndex[definition];
            var spec = specIndex >= 0 ? Package.MethodSpecs[specIndex] : new Next.BinaryMetadata.Il2CppMethodSpec { ClassIndexIndex = -1, MethodIndexIndex = -1 };
            var classArgs = Native.Arguments(spec.ClassIndexIndex);
            var methodArgs = Native.Arguments(spec.MethodIndexIndex);
            var complete = true;
            CppType Storage(TypeInfo type)
            {
                complete &= Native.IsSignatureTypeComplete(type);
                return type == null ? Cpp.GetType("void *") : AsCType(type);
            }
            var parameters = new List<(string Name, CppType Type)>();
            if (!method.IsStatic)
            {
                var declaring = method.DeclaringType;
                if (classArgs.Length > 0)
                    declaring = declaring.MakeGenericType(classArgs.Select(a => Reflection.TypesByReferenceIndex[a]).ToArray());
                parameters.Add(("__this", Storage(declaring.IsValueType ? declaring.MakeByRefType() : declaring)));
            }
            for (var i = 0; i < d.ParameterCount; i++)
                parameters.Add(($"arg{i}", Storage(Native.ResolveType(Package.Params[d.ParameterStart + i].TypeIndex, classArgs, methodArgs))));
            parameters.Add(("method", Cpp.GetType("MethodInfo *")));
            var signature = new CppFnPtrType(64, Storage(Native.ResolveType(d.ReturnType, classArgs, methodArgs)), parameters) { Name = Native.MethodName(definition, specIndex) };
            return new NativeMethod(signature.Name, address, signature, complete);
        }

        private void RegisterTypes()
        {
            if (typesBuilt)
            {
                return;
            }

            group = "unused_concrete_types";
            foreach (var type in Reflection.TypesByDefinitionIndex.Concat(Reflection.TypesByReferenceIndex).Concat(Native.GenericTypes.Values).Distinct())
            {
                AddType(type);
            }
            // Method contexts may contain constructed classes not present in the type-reference table.
            foreach (var spec in Package.MethodSpecs.Where(s => s.ClassIndexIndex >= 0).Distinct())
            {
                var definition = Reflection.MethodsByDefinitionIndex[spec.MethodDefinitionIndex].DeclaringType;
                AddType(definition.MakeGenericType(Reflection.ResolveGenericArguments(Package.GenericInstances[spec.ClassIndexIndex])));
            }
            foreach (var usage in Package.MetadataUsages.Where(u => u.Type is MetadataUsageType.TypeInfo or MetadataUsageType.Type))
            {
                var type = Reflection.GetMetadataUsageType(usage);
                var entry = model.AnalysisTypes[type];
                if (entry.Group == "unused_concrete_types")
                {
                    entry.Group = "types_from_usages";
                }

                if (usage.Type == MetadataUsageType.TypeInfo)
                {
                    entry.TypeClassAddress = usage.VirtualAddress;
                }
                else
                {
                    entry.TypeRefPtrAddress = usage.VirtualAddress;
                }
            }
            typesBuilt = true;

            void AddType(TypeInfo type)
            {
                if (type == null || type.Name == "<Module>" || model.AnalysisTypes.ContainsKey(type))
                {
                    return;
                }

                CppComplexType value = null,
                    boxed = null;
                if (!type.ContainsGenericParameters && !type.IsPointer && !type.IsByRef && (!type.IsGenericType || type.IsGenericTypeDefinition || Native.GenericOrdinals.ContainsKey(type)))
                {
                    if (type.IsArray)
                    {
                        boxed = Native.ArrayName(type) == null ? null : Array(type);
                    }
                    else
                    {
                        value =
                            type.IsEnum ? Enum(type)
                            : type.IsValueType ? Storage(type, false)
                            : null;
                        boxed = type.IsValueType ? Boxed(type) : Object(type);
                        Class(type);
                        RegisterStaticRegions(type);
                    }
                }
                var entry = new AppType(type, boxed, value) { Group = value?.Group ?? boxed?.Group ?? "types_from_usages" };
                if (boxed == null)
                {
                    model.AnalysisTypes.Add(type, entry);
                }
                else
                {
                    model.AnalysisTypes.Add(type, boxed, entry);
                }
            }
        }

        public void BuildOrderedTypes()
        {
            if (orderBuilt)
            {
                return;
            }

            BuildMethods();
            BuildTypes();
            model.AnalysisOrderedTypes.Clear();
            model.AnalysisForwardDefinitions.Clear();
            var visited = new HashSet<CppType>();
            var visiting = new HashSet<CppType>();
            var forwards = new HashSet<string>();
            foreach (var type in Cpp.Types.Values.ToArray())
            {
                Visit(type);
            }

            orderBuilt = true;

            void Visit(CppType type)
            {
                if (type is CppAlias alias)
                {
                    Visit(alias.ElementType);
                    return;
                }
                if (type is CppArrayType array)
                {
                    Visit(array.ElementType);
                    return;
                }
                if (type is CppPointerType pointer)
                {
                    if (pointer.ElementType is CppComplexType and not CppEnumType && pointer.ElementType.Name.StartsWith("Zzz_", StringComparison.Ordinal) && forwards.Add(pointer.ElementType.Name))
                    {
                        model.AnalysisForwardDefinitions.Add(new CppForwardDefinitionType(pointer.ElementType.Name) { Group = "required_forward_definitions" });
                    }

                    return;
                }
                if (type is CppFnPtrType || visited.Contains(type))
                {
                    return;
                }

                if (!visiting.Add(type))
                {
                    throw new InvalidDataException("Cyclic ZZZ C++ value layout.");
                }

                if (type is CppComplexType complex)
                {
                    foreach (var field in complex.Fields.Values.SelectMany(f => f))
                    {
                        Visit(field.Type);
                    }
                }

                visiting.Remove(type);
                visited.Add(type);
                if (type.Group != "primitive" && type.Name.StartsWith("Zzz_", StringComparison.Ordinal))
                {
                    model.AnalysisOrderedTypes.Add(type);
                }
            }
        }

        private CppType AsCType(TypeInfo type)
        {
            if (type.HasElementType)
            {
                if (type.IsArray)
                {
                    return Native.ArrayName(type) == null ? Cpp.GetType("Il2CppArray *") : Array(type).AsPointer(64);
                }

                if (type.ElementType.ContainsGenericParameters)
                {
                    return Cpp.GetType("void *");
                }

                if (type.ElementType is { IsValueType: true, IsGenericType: true, IsEnum: false } element && Native.CType(element) == "void *")
                {
                    return Native.GenericOrdinals.ContainsKey(element) ? Storage(element, false).AsPointer(64) : Cpp.GetType("void *");
                }

                return AsCType(type.ElementType).AsPointer(64);
            }
            if (type.IsEnum && (!type.IsGenericType || type.IsGenericTypeDefinition || Native.GenericOrdinals.ContainsKey(type)))
            {
                return Enum(type);
            }

            var name = Native.CType(type);
            if (!name.StartsWith("struct ", StringComparison.Ordinal))
            {
                return Cpp.GetType(name);
            }

            return type.IsValueType ? Storage(type, false) : Object(type).AsPointer(64);
        }

        private CppComplexType Enum(TypeInfo type)
        {
            var name = Native.Name(type);
            if (Cpp.Types.TryGetValue(name, out var known))
            {
                return (CppComplexType)known;
            }

            var result = Cpp.Enum(AsCType(type.GetEnumUnderlyingType()), name);
            result.Group = group;
            var names = new HashSet<string>();
            foreach (var field in type.DeclaredFields.Where(f => f.IsLiteral))
            {
                var identifier = ZzzHeaderWriter.FieldName(field.Name);
                while (!names.Add(identifier))
                {
                    identifier += "_" + field.Index;
                }

                result.AddField(identifier, field.DefaultValue);
            }
            type.ReleaseGeneratedFields();
            return result;
        }

        private CppComplexType Object(TypeInfo type) =>
            Lazy(
                Native.Name(type),
                InstanceSize(type),
                node =>
                {
                    Add(node, "klass", Class(type).AsPointer(64), 0);
                    Add(node, "monitor", Cpp.GetType("void *"), 8);
                    Add(node, "fields", Storage(type, true), 16);
                }
            );

        private CppComplexType Boxed(TypeInfo type) =>
            Lazy(
                Native.Name(type) + "__Boxed",
                InstanceSize(type),
                node =>
                {
                    Add(node, "klass", Class(type).AsPointer(64), 0);
                    Add(node, "monitor", Cpp.GetType("void *"), 8);
                    Add(node, type.IsEnum ? "value" : "fields", type.IsEnum ? Enum(type) : Storage(type, false), 16);
                }
            );

        private int InstanceSize(TypeInfo type) => type.IsGenericType && !type.IsEnum && !type.IsGenericTypeDefinition && !type.GameLayoutGroup.HasValue ? 0 : checked((int)type.Sizes.InstanceSize);

        private CppComplexType Storage(TypeInfo type, bool referenceFields)
        {
            var name = Native.Name(type) + (referenceFields ? "__Fields" : "");
            var result = Lazy(
                name,
                Math.Max(0, InstanceSize(type) - 16),
                node =>
                {
                    if (InstanceSize(type) == 0)
                    {
                        return;
                    }

                    var hierarchy = new List<TypeInfo>();
                    for (var current = type; current != null; current = current.BaseType)
                    {
                        hierarchy.Add(current);
                    }

                    hierarchy.Reverse();
                    var names = new HashSet<string>();
                    foreach (var current in hierarchy)
                    {
                        if (current.ContainsGenericParameters || InstanceSize(current) == 0)
                        {
                            continue;
                        }

                        foreach (var field in current.DeclaredFields.Where(f => !f.IsLiteral && !f.IsStatic))
                        {
                            var name = ZzzHeaderWriter.FieldName(field.Name);
                            while (!names.Add(name))
                            {
                                name += "_" + field.Index;
                            }

                            var offset = checked((int)field.Offset) - (type.IsValueType ? 0 : 16);
                            if (offset < 0)
                            {
                                continue;
                            }

                            var storage = AsCType(field.FieldType);
                            if (field.FieldType.IsValueType && storage.Name == "void *")
                            {
                                storage = Cpp.GetType("uint8_t").AsArray(Math.Max(0, checked((int)field.FieldType.Sizes.InstanceSize) - 16));
                            }

                            Add(node, name, storage, offset);
                        }
                        current.ReleaseGeneratedFields();
                    }
                }
            );
            if (type.IsValueType && InstanceSize(type) > 0)
            {
                var layout = type.GameLayoutGroup ?? Adapter.DefinitionLayoutGroup(type.Index);
                result.AlignmentBytes = Adapter.LayoutAlignment(layout);
            }
            return result;
        }

        private CppComplexType Array(TypeInfo type)
        {
            var name = Native.ArrayName(type);
            return Lazy(
                name,
                32 + ElementSize(type.ElementType) * 32,
                node =>
                {
                    Add(node, "klass", Cpp.GetType("Il2CppClass *"), 0);
                    Add(node, "monitor", Cpp.GetType("void *"), 8);
                    Add(node, "bounds", Cpp.GetType("Il2CppArrayBounds *"), 16);
                    Add(node, "max_length", Cpp.GetType("il2cpp_array_size_t"), 24);
                    Add(node, "vector", AsCType(type.ElementType).AsArray(32), 32);
                }
            );
        }

        private static int ElementSize(TypeInfo type) => type.HasElementType || !type.IsValueType ? 8 : checked((int)type.Sizes.InstanceSize - 16);

        private CppComplexType Class(TypeInfo type)
        {
            var definition = type.Definition.IsValid ? type.Definition : type.GetGenericTypeDefinition().Definition;
            var split = type.IsGenericType && !type.IsGenericTypeDefinition || Adapter.DefinitionHasSplitVTable(definition);
            var count = definition.VTableCount;
            if (type.IsInterface && definition.MethodCount > 0)
            {
                var view = Lazy(
                    $"Zzz_InterfaceVTable_{type.Index}",
                    definition.MethodCount * 8,
                    view =>
                    {
                        Add(view, "methodPtr", Cpp.GetType("Il2CppMethodPointer").AsArray(definition.MethodCount), 0);
                        for (var slot = 0; slot < definition.MethodCount; slot++)
                        {
                            Add(
                                view,
                                $"methodPtr_{slot}_{ZzzHeaderWriter.FieldName(Reflection.MethodsByDefinitionIndex[definition.MethodIndex + slot].Name)}",
                                Cpp.GetType("Il2CppMethodPointer"),
                                slot * 8
                            );
                        }
                    }
                );
                Cpp.TypedefAliases.TryAdd(Native.Name(type) + "__InterfaceVTable", view);
                if (count == 0)
                {
                    Cpp.TypedefAliases.TryAdd(Native.Name(type) + "__VTable", view);
                }
            }
            var table =
                count == 0
                    ? null
                    : Lazy(
                        $"Zzz_VTable_{type.Index}_{(split ? "Dual" : "Single")}",
                        count * (split ? 16 : 8),
                        table =>
                        {
                            Add(table, "methodPtr", Cpp.GetType("Il2CppMethodPointer").AsArray(count), 0);
                            if (split)
                            {
                                Add(table, "method", Cpp.GetType("MethodInfo *").AsArray(count), count * 8);
                            }

                            for (var slot = 0; slot < count; slot++)
                            {
                                var usage = MetadataUsage.FromEncodedIndex(Package, Package.VTableMethodIndices[definition.VTableIndex + slot]);
                                var method =
                                    !usage.IsValid ? -1
                                    : usage.Type == MetadataUsageType.MethodRef ? Package.MethodSpecs[usage.SourceIndex].MethodDefinitionIndex
                                    : usage.SourceIndex;
                                var name = method < 0 ? "unknown" : ZzzHeaderWriter.FieldName(Reflection.MethodsByDefinitionIndex[method].Name);
                                Add(table, $"methodPtr_{slot}_{name}", Cpp.GetType("Il2CppMethodPointer"), slot * 8);
                                if (split)
                                {
                                    Add(table, $"method_{slot}_{name}", Cpp.GetType("MethodInfo *"), (count + slot) * 8);
                                }
                            }
                        }
                    );
            if (table != null)
            {
                Cpp.TypedefAliases.TryAdd(Native.Name(type) + "__VTable", table);
            }

            return Lazy(
                Native.Name(type) + "__Class",
                0xD0 + count * (split ? 16 : 8),
                node =>
                {
                    Add(node, "header", Cpp.GetType("Il2CppClass_0"), 0);
                    if (table != null)
                    {
                        Add(node, "vtable", table, 0xD0);
                    }
                }
            );
        }

        private CppComplexType Lazy(string name, int size, Action<CppComplexType> fill)
        {
            if (Cpp.Types.TryGetValue(name, out var known))
            {
                if (known.Group == "unused_concrete_types")
                {
                    known.Group = group;
                }

                return (CppComplexType)known;
            }
            var result = new DeferredStruct(size, fill) { Name = name, Group = group };
            Cpp.Types.Add(name, result);
            return result;
        }

        private void RegisterStaticRegions(TypeInfo type)
        {
            if (InstanceSize(type) == 0)
            {
                return;
            }

            var definition = type.Definition.IsValid ? type.Definition : type.GetGenericTypeDefinition().Definition;
            var layout = type.GameLayoutGroup ?? Adapter.DefinitionLayoutGroup(type.Index);
            var tags = new HashSet<int>();
            for (var local = 0; local < definition.FieldCount; local++)
            {
                var field = Package.Fields[definition.FieldIndex + local];
                var attributes = (uint)Package.TypeReferences[field.TypeIndex].Attrs;
                if ((attributes & 0x50) != 0x10)
                {
                    continue;
                }

                tags.Add((int)(Adapter.LayoutRawFieldOffset(layout, local) >> 24));
            }
            foreach (var tag in tags)
            {
                var suffix = tag switch
                {
                    0 => "__StaticFields",
                    1 => "__ThreadStaticFields",
                    _ => $"__GlobalStaticFields{tag}",
                };
                Lazy(
                    Native.Name(type) + suffix,
                    -1,
                    node =>
                    {
                        var names = new HashSet<string>();
                        foreach (var field in type.DeclaredFields.Where(f => f.IsStatic && !f.IsLiteral && f.ZzzStorageTag == tag))
                        {
                            var name = ZzzHeaderWriter.FieldName(field.Name);
                            while (!names.Add(name))
                            {
                                name += "_" + field.Index;
                            }

                            Add(node, name, AsCType(field.FieldType), checked((int)field.Offset));
                        }
                        type.ReleaseGeneratedFields();
                    }
                );
            }
        }

        private static void Add(CppComplexType node, string name, CppType type, int offset)
        {
            var field = new CppField(name, type) { Offset = checked(offset * 8) };
            if (!node.Fields.TryGetValue(field.Offset, out var fields))
            {
                node.Fields.Add(field.Offset, fields = []);
            }

            fields.Add(field);
        }

        private sealed class DeferredStruct(int bytes, Action<CppComplexType> fill) : CppComplexType(ComplexValueType.Struct)
        {
            private bool initialized;
            private int? releasedSize;
            public override int Size
            {
                get =>
                    bytes >= 0 ? checked(bytes * 8)
                    : !initialized && releasedSize.HasValue ? releasedSize.Value
                    : Fields.Values.SelectMany(f => f).Select(f => f.Offset + f.Type.Size).DefaultIfEmpty(0).Max();
                set { }
            }

            public override void ReleaseTransientFields()
            {
                if (!initialized)
                    return;
                releasedSize = Size;
                base.Fields.Clear();
                initialized = false;
            }

            public override SortedDictionary<int, List<CppField>> Fields
            {
                get
                {
                    if (!initialized)
                    {
                        initialized = true;
                        try
                        {
                            fill(this);
                        }
                        catch
                        {
                            base.Fields.Clear();
                            initialized = false;
                            throw;
                        }
                    }
                    return base.Fields;
                }
                internal set => base.Fields = value;
            }

            public override string ToString(string format = "")
            {
                if (Size == 0)
                {
                    return $"struct {Name};\n";
                }

                var alignment = AlignmentBytes > 0 ? $" __declspec(align({AlignmentBytes}))" : "";
                var output = new StringBuilder($"#pragma pack(push, 1)\nstruct{alignment} {Name} {{\n");
                var fields = Fields.Values.SelectMany(f => f).OrderBy(f => f.OffsetBytes).ToArray();
                var cursor = 0;
                var padding = 0;
                for (var i = 0; i < fields.Length; )
                {
                    var field = fields[i];
                    if (field.OffsetBytes > cursor)
                    {
                        output.AppendLine($"    uint8_t __padding_{padding++}[{field.OffsetBytes - cursor}];");
                    }

                    var end = field.OffsetBytes + field.SizeBytes;
                    var j = i + 1;
                    while (j < fields.Length && fields[j].OffsetBytes < end)
                    {
                        end = Math.Max(end, fields[j].OffsetBytes + fields[j].SizeBytes);
                        j++;
                    }
                    if (j == i + 1)
                    {
                        output.AppendLine($"    {field.Type.ToFieldString(field.Name, format)};");
                    }
                    else
                    {
                        output.AppendLine("    union {");
                        for (var n = i; n < j; n++)
                        {
                            var member = fields[n];
                            var declaration = member.Type.ToFieldString(member.Name, format);
                            if (member.OffsetBytes == field.OffsetBytes)
                            {
                                output.AppendLine($"        {declaration};");
                            }
                            else
                            {
                                output.AppendLine($"        struct {{ uint8_t __padding_{padding++}[{member.OffsetBytes - field.OffsetBytes}]; {declaration}; }};");
                            }
                        }
                        output.AppendLine("    };");
                    }
                    cursor = Math.Max(cursor, end);
                    i = j;
                }
                if (SizeBytes > cursor)
                {
                    output.AppendLine($"    uint8_t __padding_{padding}[{SizeBytes - cursor}];");
                }

                return output.AppendLine("};\n#pragma pack(pop)").ToString();
            }
        }
    }
}
