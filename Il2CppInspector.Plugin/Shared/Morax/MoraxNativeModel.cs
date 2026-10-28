using Il2CppInspector.Cpp;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model;

/// <summary>Layout-group alignment and the inline vtable class shape shared by the MORAX native models.</summary>
internal abstract class MoraxNativeModel : NativeTypeModel
{
    private MoraxAdapter Adapter => (MoraxAdapter)Package.Metadata.GameAdapter;
    public override Dictionary<TypeInfo, int> GenericOrdinals { get; }
    public override Dictionary<int, TypeInfo> GenericTypes { get; }

    protected MoraxNativeModel(TypeModel model)
        : base(model)
    {
        var layouts = (SharedTypeLayouts)model.GameLayouts;
        GenericOrdinals = layouts.GenericOrdinals;
        GenericTypes = layouts.GenericTypes;
    }

    internal override int Alignment(TypeInfo type) => Adapter.LayoutAlignment(type.GameLayoutGroup ?? Adapter.DefinitionLayoutGroup(type.Index));

    /// <summary>
    /// A 0xD0-byte class header followed by the vtable: method pointers, then MethodInfo* when split,
    /// then any extra method pointer slots.
    /// </summary>
    private protected CppComplexType DeclareInlineVTableClass(NativeLayoutAnalysisModel layouts, TypeInfo type, bool split, int extra)
    {
        var cpp = layouts.Cpp;
        var definition = type.Definition.IsValid ? type.Definition : type.GetGenericTypeDefinition().Definition;
        var count = definition.VTableCount;
        if (type.IsInterface && definition.MethodCount > 0)
        {
            var view = layouts.Declare(
                InterfaceVTableName(type),
                definition.MethodCount * 8,
                view =>
                {
                    NativeLayoutAnalysisModel.Add(view, "methodPtr", cpp.GetType("Il2CppMethodPointer").AsArray(definition.MethodCount), 0);
                    for (var slot = 0; slot < definition.MethodCount; slot++)
                    {
                        NativeLayoutAnalysisModel.Add(
                            view,
                            $"methodPtr_{slot}_{CppDeclarationGenerator.FieldIdentifier(Model.MethodsByDefinitionIndex[definition.MethodIndex + slot].Name)}",
                            cpp.GetType("Il2CppMethodPointer"),
                            slot * 8
                        );
                    }
                }
            );
            cpp.TypedefAliases.TryAdd(Name(type) + "__InterfaceVTable", view);
            if (count == 0)
                cpp.TypedefAliases.TryAdd(Name(type) + "__VTable", view);
        }
        var table =
            count == 0
                ? null
                : layouts.Declare(
                    VTableName(type, split),
                    count * (split ? 16 : 8),
                    table =>
                    {
                        NativeLayoutAnalysisModel.Add(table, "methodPtr", cpp.GetType("Il2CppMethodPointer").AsArray(count), 0);
                        if (split)
                            NativeLayoutAnalysisModel.Add(table, "method", cpp.GetType("MethodInfo *").AsArray(count), count * 8);
                        for (var slot = 0; slot < count; slot++)
                        {
                            var name = VTableSlotName(definition, slot);
                            NativeLayoutAnalysisModel.Add(table, $"methodPtr_{slot}_{name}", cpp.GetType("Il2CppMethodPointer"), slot * 8);
                            if (split)
                                NativeLayoutAnalysisModel.Add(table, $"method_{slot}_{name}", cpp.GetType("MethodInfo *"), (count + slot) * 8);
                        }
                    }
                );
        if (table != null)
            cpp.TypedefAliases.TryAdd(Name(type) + "__VTable", table);
        var vtableSize = count * (split ? 16 : 8);
        return layouts.Declare(
            Name(type) + "__Class",
            0xD0 + vtableSize + extra * 8,
            node =>
            {
                NativeLayoutAnalysisModel.Add(node, "header", cpp.GetType("Il2CppClass_0"), 0);
                if (table != null)
                    NativeLayoutAnalysisModel.Add(node, "vtable", table, 0xD0);
                if (extra > 0)
                    NativeLayoutAnalysisModel.Add(node, "extraVTableSlots", cpp.GetType("Il2CppMethodPointer").AsArray(extra), 0xD0 + vtableSize);
            }
        );
    }

    /// <summary>Vtable entries use the metadata-usage encoding; empty slots are named "unknown".</summary>
    private protected string VTableSlotName(Next.Metadata.Il2CppTypeDefinition definition, int slot)
    {
        var index = definition.VTableIndex < 0 ? -1 : definition.VTableIndex + slot;
        var usage =
            index >= 0 && index < Package.VTableMethodIndices.Length
                ? MetadataUsage.FromEncodedIndex(Package, Package.VTableMethodIndices[index])
                : default;
        var method =
            !usage.IsValid ? -1
            : usage.Type == MetadataUsageType.MethodRef && usage.SourceIndex < Package.MethodSpecs.Length
                ? Package.MethodSpecs[usage.SourceIndex].MethodDefinitionIndex
            : usage.SourceIndex;
        return method < 0 || method >= Model.MethodsByDefinitionIndex.Length
            ? "unknown"
            : CppDeclarationGenerator.FieldIdentifier(Model.MethodsByDefinitionIndex[method].Name);
    }
}
