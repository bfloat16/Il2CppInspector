using Il2CppInspector.Cpp;
using Il2CppInspector.Model;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.TOT
{
    /// <summary>
    /// TOT analysis model. This mirrors the stock AppModel.Build analysis loops (methods, generic
    /// methods, metadata usages and unused concrete types) so that a caller that keeps the game
    /// adapter active still gets a populated C++ model. In normal operation TOT suppresses the adapter
    /// after loading, so the stock pipeline performs this work and these methods are not invoked.
    /// </summary>
    internal sealed class TotAnalysisModel(AppModel model) : Plugins.IGameAnalysisModel
    {
        private CppDeclarationGenerator generator;
        private bool methodsBuilt;
        private bool typesBuilt;
        private bool orderBuilt;
        private string group = "unused_concrete_types";

        private Il2CppInspector Package => model.Package;
        private TypeModel Reflection => model.TypeModel;
        private CppDeclarationGenerator Generator => generator ??= new CppDeclarationGenerator(model);

        public void BuildMethods()
        {
            if (methodsBuilt)
            {
                return;
            }

            methodsBuilt = true;
            group = "types_from_methods";
            foreach (var method in Reflection.MethodsByDefinitionIndex.Where(m => m.VirtualAddress.HasValue))
            {
                Include(method);
            }

            group = "types_from_generic_methods";
            foreach (var method in Reflection.GenericMethods.Values.Where(m => m.VirtualAddress.HasValue))
            {
                Include(method);
            }

            group = "types_from_usages";
            if (Package.MetadataUsages != null)
            {
                foreach (var usage in Package.MetadataUsages)
                {
                    switch (usage.Type)
                    {
                        case MetadataUsageType.StringLiteral:
                            model.Strings[usage.VirtualAddress] = Reflection.GetMetadataUsageName(usage);
                            break;
                        case MetadataUsageType.Type:
                        case MetadataUsageType.TypeInfo:
                        {
                            var type = Reflection.GetMetadataUsageType(usage);
                            Generator.IncludeType(type);
                            AddTypes(Generator.GenerateRemainingTypeDeclarations());
                            if (!model.AnalysisTypes.ContainsKey(type))
                            {
                                model.AnalysisTypes.Add(type, new AppType(type, null) { Group = group });
                            }

                            if (usage.Type == MetadataUsageType.TypeInfo)
                            {
                                model.AnalysisTypes[type].TypeClassAddress = usage.VirtualAddress;
                            }
                            else
                            {
                                model.AnalysisTypes[type].TypeRefPtrAddress = usage.VirtualAddress;
                            }

                            break;
                        }
                        case MetadataUsageType.MethodDef:
                        case MetadataUsageType.MethodRef:
                        {
                            var method = Reflection.GetMetadataUsageMethod(usage);
                            Include(method);
                            model.AnalysisMethods[method].MethodInfoPtrAddress = usage.VirtualAddress;
                            break;
                        }
                        case MetadataUsageType.FieldInfo:
                        case MetadataUsageType.FieldRva:
                        {
                            var fieldRef = Package.FieldRefs[usage.SourceIndex];
                            var fieldType = Reflection.GetMetadataUsageType(usage);
                            var field = fieldType.DeclaredFields.First(f => f.Index == fieldType.Definition.FieldIndex + fieldRef.FieldIndex);
                            var value =
                                field.HasFieldRVA && field.DefaultValueMetadataAddress != 0
                                    ? Convert.ToHexString(Package.Metadata.ReadBytes((long)field.DefaultValueMetadataAddress, field.FieldType.Sizes.NativeSize))
                                    : "";
                            if (usage.Type == MetadataUsageType.FieldInfo)
                            {
                                model.Fields[usage.VirtualAddress] = (field, value);
                            }
                            else
                            {
                                model.FieldRvas[usage.VirtualAddress] = (field, value);
                            }

                            break;
                        }
                    }
                }
            }
        }

        private void Include(MethodBase method)
        {
            Generator.IncludeMethod(method);
            AddTypes(Generator.GenerateRemainingTypeDeclarations());
            var pointer = Generator.GenerateMethodDeclaration(method);
            if (!model.AnalysisMethods.ContainsKey(method))
            {
                model.AnalysisMethods.Add(method, pointer, new AppMethod(method, pointer) { Group = group });
            }
        }

        public void BuildTypes()
        {
            if (typesBuilt)
            {
                return;
            }

            typesBuilt = true;
            BuildMethods();
            group = "unused_concrete_types";

            var used = model.AnalysisTypes.Values.Select(t => t.Type).ToHashSet();
            var unused = Reflection.Types.Where(t =>
                !used.Contains(t) && !t.IsGenericType && !t.IsGenericParameter && !t.IsByRef && !t.IsPointer && !t.IsArray && !t.IsAbstract && t.Name != "<Module>"
            );
            foreach (var type in unused)
            {
                Generator.IncludeType(type);
            }

            AddTypes(Generator.GenerateRemainingTypeDeclarations());
        }

        public void BuildOrderedTypes()
        {
            if (orderBuilt)
            {
                return;
            }

            orderBuilt = true;
            BuildTypes();
            model.AnalysisForwardDefinitions.Clear();
            model.AnalysisForwardDefinitions.AddRange(Generator.GenerateRequiredForwardDefinitions());
        }

        public IEnumerable<NativeMethod> EnumerateNativeMethods() =>
            model.AnalysisMethods.Values.Where(m => m.HasCompiledCode).Select(m => new NativeMethod(m.ToMangledString(), m.MethodCodeAddress, m.CppFnPtrType));

        public IEnumerable<CppType> EnumerateNativeTypes() => model.RuntimeCppTypes.Types.Values;

        private void AddTypes(List<(TypeInfo ilType, CppComplexType valueType, CppComplexType referenceType, CppComplexType fieldsType, CppComplexType vtableType, CppComplexType staticsType)> types)
        {
            foreach (var type in types)
            {
                if (type.vtableType != null)
                {
                    model.AnalysisOrderedTypes.Add(type.vtableType);
                }

                if (type.staticsType != null)
                {
                    model.AnalysisOrderedTypes.Add(type.staticsType);
                }

                if (type.fieldsType != null)
                {
                    model.AnalysisOrderedTypes.Add(type.fieldsType);
                }

                if (type.valueType != null)
                {
                    model.AnalysisOrderedTypes.Add(type.valueType);
                }

                model.AnalysisOrderedTypes.Add(type.referenceType);
            }

            foreach (var type in types)
            {
                if (!model.AnalysisTypes.ContainsKey(type.ilType))
                {
                    model.AnalysisTypes.Add(type.ilType, type.referenceType, new AppType(type.ilType, type.referenceType, type.valueType) { Group = group });
                }
            }
        }
    }
}
