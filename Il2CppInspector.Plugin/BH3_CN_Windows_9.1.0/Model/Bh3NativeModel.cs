using Il2CppInspector.Cpp;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model
{
    // Only recovered MORAX descriptor and class/vtable layout differences belong here.
    internal sealed class Bh3NativeModel : NativeTypeModel
    {
        private Bh3Morax Adapter => (Bh3Morax)Package.Metadata.GameAdapter;
        public override Dictionary<TypeInfo, int> GenericOrdinals { get; }
        public override Dictionary<int, TypeInfo> GenericTypes { get; }

        internal Bh3NativeModel(TypeModel model)
            : base(model)
        {
            var layouts = (SharedTypeLayouts)model.GameLayouts;
            GenericOrdinals = layouts.GenericOrdinals;
            GenericTypes = layouts.GenericTypes;
        }

        internal override int Alignment(TypeInfo type) => Adapter.LayoutAlignment(type.GameLayoutGroup ?? Adapter.DefinitionLayoutGroup(type.Index));

        // D §5.3 / §A.5 / §B.2: the BH3 Il2CppClass is 0xD8 bytes with the vtable at +0x00 and 16-byte
        // slots (B §A.5: pool_alloc(2 * vtableCount, 8) = method pointer + MethodInfo*), the name at
        // +0x20, properties at +0x28, typeDefinition at +0x38, fields at +0x50, image at +0x58,
        // interface offsets at +0x70, events at +0x78 and byval_arg / this_arg at +0x88 / +0x98.
        internal override CppComplexType CreateClass(NativeLayoutAnalysisModel layouts, TypeInfo type)
        {
            var Cpp = layouts.Cpp;
            var definition = type.Definition.IsValid ? type.Definition : type.GetGenericTypeDefinition().Definition;
            var count = definition.VTableCount;
            var table =
                count == 0
                    ? null
                    : layouts.Declare(
                        VTableName(type, true),
                        count * 16,
                        table =>
                        {
                            NativeLayoutAnalysisModel.Add(table, "methodPtr", Cpp.GetType("Il2CppMethodPointer").AsArray(count), 0);
                            NativeLayoutAnalysisModel.Add(table, "method", Cpp.GetType("MethodInfo *").AsArray(count), count * 8);
                            for (var slot = 0; slot < count; slot++)
                            {
                                // Empty slots retain an unknown name; populated entries use the
                                // same method definition/spec ordinals as the usage resolver.
                                var index = definition.VTableIndex < 0 ? -1 : definition.VTableIndex + slot;
                                var usage = index >= 0 && index < Package.VTableMethodIndices.Length ? MetadataUsage.FromEncodedIndex(Package, Package.VTableMethodIndices[index]) : default;
                                var method =
                                    !usage.IsValid ? -1
                                    : usage.Type == MetadataUsageType.MethodRef && usage.SourceIndex < Package.MethodSpecs.Length ? Package.MethodSpecs[usage.SourceIndex].MethodDefinitionIndex
                                    : usage.SourceIndex;
                                var name =
                                    method < 0 || method >= Model.MethodsByDefinitionIndex.Length ? "unknown" : CppDeclarationGenerator.FieldIdentifier(Model.MethodsByDefinitionIndex[method].Name);
                                NativeLayoutAnalysisModel.Add(table, $"methodPtr_{slot}_{name}", Cpp.GetType("Il2CppMethodPointer"), slot * 8);
                                NativeLayoutAnalysisModel.Add(table, $"method_{slot}_{name}", Cpp.GetType("MethodInfo *"), (count + slot) * 8);
                            }
                        }
                    );
            if (table != null)
            {
                Cpp.TypedefAliases.TryAdd(Name(type) + "__VTable", table);
            }

            return layouts.Declare(
                Name(type) + "__Class",
                0xD8,
                node =>
                {
                    if (table != null)
                    {
                        NativeLayoutAnalysisModel.Add(node, "vtable", table.AsPointer(64), 0);
                    }

                    NativeLayoutAnalysisModel.Add(node, "header", Cpp.GetType("Il2CppClass_0"), 0);
                }
            );
        }
    }
}
