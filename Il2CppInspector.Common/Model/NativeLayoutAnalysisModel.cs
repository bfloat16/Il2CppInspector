using Il2CppInspector.Cpp;
using Il2CppInspector.Outputs;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model
{
    // Shared analysis of decoded, recorded layouts; exporters consume this same model.
    internal sealed class NativeLayoutAnalysisModel(AppModel model) : Plugins.IGameAnalysisModel
    {
        private NativeTypeModel Native => (NativeTypeModel)model.GameNativeModel;
        private Il2CppInspector Package => model.Package;
        private TypeModel Reflection => model.TypeModel;
        internal CppTypeCollection Cpp => model.RuntimeCppTypes;
        private bool methodsBuilt,
            typesBuilt,
            orderBuilt;
        private readonly HashSet<CppType> nativeTypes = [];
        private string group = "unused_concrete_types";

        public void BuildMethods()
        {
            if (methodsBuilt)
            {
                return;
            }

            // Reuse signature types while building methods without retaining another model-wide index.
            var signatureTypes = new Dictionary<TypeInfo, (CppType Type, bool Complete)>();
            model.BuildMethodsFromMetadata(AddMethod);
            methodsBuilt = true;

            AppMethod AddMethod(MethodBase method, int definition, int spec, string group)
            {
                if (model.AnalysisMethods.ContainsKey(method))
                {
                    return model.AnalysisMethods[method];
                }

                this.group = group;
                var complete = true;
                CppType Convert(TypeInfo type)
                {
                    if (!signatureTypes.TryGetValue(type, out var signature))
                    {
                        signature = (AsCType(type), Native.IsSignatureTypeComplete(type));
                        signatureTypes.Add(type, signature);
                    }
                    complete &= signature.Complete;
                    return signature.Type;
                }
                var pointer = model.NativeDeclarationGenerator.GenerateMethodDeclaration(method, Convert);
                pointer.Group = group;
                var result = new AppMethod(method, pointer) { Group = group, SignatureComplete = complete };
                model.AnalysisMethods.Add(method, pointer, result);
                method.ReleaseTransientParameters();
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
            BuildMethods();
            return model
                .AnalysisMethods.Values.Where(m => m.HasCompiledCode)
                .Select(m => new NativeMethod(m.CppFnPtrType.Name, m.MethodCodeAddress, m.CppFnPtrType, m.SignatureComplete) { SourceName = m.Method.Name, LinkageName = m.ToMangledString() });
        }

        private void RegisterTypes()
        {
            if (typesBuilt)
            {
                return;
            }

            group = "unused_concrete_types";
            foreach (var type in Reflection.TypesByDefinitionIndex.Concat(Reflection.TypesByReferenceIndex).Concat(Native.GenericTypes.Values).Concat(Native.ArrayTypes.Values.ToArray()).Distinct())
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
                if (type == null || model.AnalysisTypes.ContainsKey(type) || type.Name == "<Module>")
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
            var (ordered, forwards) = CppTypeDependencyGraph.OrderDeclarations(Cpp, nativeTypes);
            model.AnalysisOrderedTypes.AddRange(ordered);
            model.AnalysisForwardDefinitions.AddRange(forwards);
            orderBuilt = true;
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
            nativeTypes.Add(result);
            result.Group = group;
            var names = new HashSet<string>();
            foreach (var field in type.DeclaredFields.Where(f => f.IsLiteral))
            {
                var identifier = CppDeclarationGenerator.FieldIdentifier(field.Name);
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
            Declare(
                Native.Name(type),
                InstanceSize(type),
                node =>
                {
                    Add(node, "klass", Class(type).AsPointer(64), 0);
                    Add(node, "monitor", Cpp.GetType("void *"), 8);
                    if (InstanceSize(type) > 16)
                        Add(node, "fields", Storage(type, true), 16);
                }
            );

        private CppComplexType Boxed(TypeInfo type) =>
            Declare(
                Native.Name(type) + "__Boxed",
                InstanceSize(type),
                node =>
                {
                    Add(node, "klass", Class(type).AsPointer(64), 0);
                    Add(node, "monitor", Cpp.GetType("void *"), 8);
                    Add(node, type.IsEnum ? "value" : "fields", type.IsEnum ? Enum(type) : Storage(type, false), 16);
                }
            );

        private int InstanceSize(TypeInfo type) => !Native.HasInstanceLayout(type) && !type.IsEnum ? 0 : checked((int)type.Sizes.InstanceSize);

        private CppComplexType Class(TypeInfo type) => Native.CreateClass(this, type);

        private CppComplexType Storage(TypeInfo type, bool referenceFields)
        {
            var name = Native.Name(type) + (referenceFields ? "__Fields" : "");
            var result = Declare(
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
                            var name = CppDeclarationGenerator.FieldIdentifier(field.Name);
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
                            if (field.FieldType.IsValueType && field.FieldType.IsGenericType && storage.Name == "void *")
                            {
                                var bytes = checked((int)field.FieldType.Sizes.InstanceSize) - 16;
                                // Unknown inline storage remains padding in the enclosing recorded layout.
                                if (bytes <= 0)
                                    continue;
                                storage = Cpp.GetType("uint8_t").AsArray(bytes);
                            }

                            Add(node, name, storage, offset);
                        }
                        current.ReleaseGeneratedFields();
                    }
                }
            );
            if (type.IsValueType && InstanceSize(type) > 0)
            {
                result.AlignmentBytes = Native.Alignment(type);
                ((CppRecordedLayoutType)result).ExplicitLayout = (type.Attributes & System.Reflection.TypeAttributes.LayoutMask) == System.Reflection.TypeAttributes.ExplicitLayout;
            }
            return result;
        }

        private CppComplexType Array(TypeInfo type)
        {
            var name = Native.ArrayName(type);
            return Declare(
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

        internal CppComplexType Declare(string name, int size, Action<CppComplexType> fill)
        {
            if (Cpp.Types.TryGetValue(name, out var known))
            {
                if (known.Group == "unused_concrete_types")
                {
                    known.Group = group;
                }

                return (CppComplexType)known;
            }
            var result = new CppRecordedLayoutType(size, fill) { Name = name, Group = group };
            Cpp.Types.Add(name, result);
            nativeTypes.Add(result);
            return result;
        }

        private void RegisterStaticRegions(TypeInfo type)
        {
            if (InstanceSize(type) == 0)
            {
                return;
            }

            var tags = type.DeclaredFields.Where(f => f.IsStatic && !f.IsLiteral).Select(f => f.StorageTag).Distinct().ToArray();
            type.ReleaseGeneratedFields();
            foreach (var tag in tags)
            {
                var suffix = tag switch
                {
                    0 => "__StaticFields",
                    1 => "__ThreadStaticFields",
                    _ => $"__GlobalStaticFields{tag}",
                };
                Declare(
                    Native.Name(type) + suffix,
                    -1,
                    node =>
                    {
                        var names = new HashSet<string>();
                        foreach (var field in type.DeclaredFields.Where(f => f.IsStatic && !f.IsLiteral && f.StorageTag == tag))
                        {
                            var name = CppDeclarationGenerator.FieldIdentifier(field.Name);
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

        internal static void Add(CppComplexType node, string name, CppType type, int offset)
        {
            var field = new CppField(name, type) { Offset = checked(offset * 8) };
            if (!node.Fields.TryGetValue(field.Offset, out var fields))
            {
                node.Fields.Add(field.Offset, fields = []);
            }

            fields.Add(field);
        }
    }
}
