using Il2CppInspector.Cpp;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model
{
    // Only recovered MORAX descriptor and class/vtable layout differences belong here.
    internal sealed class GenshinNativeModel : NativeTypeModel
    {
        private GenshinMorax Adapter => (GenshinMorax)Package.Metadata.GameAdapter;
        public override Dictionary<TypeInfo, int> GenericOrdinals { get; }
        public override Dictionary<int, TypeInfo> GenericTypes { get; }

        internal GenshinNativeModel(TypeModel model)
            : base(model)
        {
            var layouts = (SharedTypeLayouts)model.GameLayouts;
            GenericOrdinals = layouts.GenericOrdinals;
            GenericTypes = layouts.GenericTypes;
        }

        internal override int Alignment(TypeInfo type) => Adapter.LayoutAlignment(type.GameLayoutGroup ?? Adapter.DefinitionLayoutGroup(type.Index));

        internal override CppComplexType CreateClass(NativeLayoutAnalysisModel layouts, TypeInfo type)
        {
            var Cpp = layouts.Cpp;
            var definition = type.Definition.IsValid ? type.Definition : type.GetGenericTypeDefinition().Definition;
            var split = type.IsGenericType && !type.IsGenericTypeDefinition || Adapter.DefinitionHasSplitVTable(definition);
            var count = definition.VTableCount;
            var extra = Adapter.DefinitionExtraVTableSlots((int)(definition.Token & 0xFFFFFF));
            if (type.IsInterface && definition.MethodCount > 0)
            {
                var view = layouts.Declare(
                    InterfaceVTableName(type),
                    definition.MethodCount * 8,
                    view =>
                    {
                        NativeLayoutAnalysisModel.Add(view, "methodPtr", Cpp.GetType("Il2CppMethodPointer").AsArray(definition.MethodCount), 0);
                        for (var slot = 0; slot < definition.MethodCount; slot++)
                        {
                            NativeLayoutAnalysisModel.Add(
                                view,
                                $"methodPtr_{slot}_{CppDeclarationGenerator.FieldIdentifier(Model.MethodsByDefinitionIndex[definition.MethodIndex + slot].Name)}",
                                Cpp.GetType("Il2CppMethodPointer"),
                                slot * 8
                            );
                        }
                    }
                );
                Cpp.TypedefAliases.TryAdd(Name(type) + "__InterfaceVTable", view);
                if (count == 0)
                {
                    Cpp.TypedefAliases.TryAdd(Name(type) + "__VTable", view);
                }
            }
            var table =
                count == 0
                    ? null
                    : layouts.Declare(
                        VTableName(type, split),
                        count * (split ? 16 : 8),
                        table =>
                        {
                            NativeLayoutAnalysisModel.Add(table, "methodPtr", Cpp.GetType("Il2CppMethodPointer").AsArray(count), 0);
                            if (split)
                            {
                                NativeLayoutAnalysisModel.Add(table, "method", Cpp.GetType("MethodInfo *").AsArray(count), count * 8);
                            }

                            for (var slot = 0; slot < count; slot++)
                            {
                                var usage = MetadataUsage.FromEncodedIndex(Package, Package.VTableMethodIndices[definition.VTableIndex + slot]);
                                var method =
                                    !usage.IsValid ? -1
                                    : usage.Type == MetadataUsageType.MethodRef ? Package.MethodSpecs[usage.SourceIndex].MethodDefinitionIndex
                                    : usage.SourceIndex;
                                var name = method < 0 ? "unknown" : CppDeclarationGenerator.FieldIdentifier(Model.MethodsByDefinitionIndex[method].Name);
                                NativeLayoutAnalysisModel.Add(table, $"methodPtr_{slot}_{name}", Cpp.GetType("Il2CppMethodPointer"), slot * 8);
                                if (split)
                                {
                                    NativeLayoutAnalysisModel.Add(table, $"method_{slot}_{name}", Cpp.GetType("MethodInfo *"), (count + slot) * 8);
                                }
                            }
                        }
                    );
            if (table != null)
            {
                Cpp.TypedefAliases.TryAdd(Name(type) + "__VTable", table);
            }

            return layouts.Declare(
                Name(type) + "__Class",
                0xD0 + count * (split ? 16 : 8) + extra * 8,
                node =>
                {
                    NativeLayoutAnalysisModel.Add(node, "header", Cpp.GetType("Il2CppClass_0"), 0);
                    if (table != null)
                    {
                        NativeLayoutAnalysisModel.Add(node, "vtable", table, 0xD0);
                    }
                    if (extra > 0)
                    {
                        NativeLayoutAnalysisModel.Add(node, "extraVTableSlots", Cpp.GetType("Il2CppMethodPointer").AsArray(extra), 0xD0 + count * (split ? 16 : 8));
                    }
                }
            );
        }
    }
}
