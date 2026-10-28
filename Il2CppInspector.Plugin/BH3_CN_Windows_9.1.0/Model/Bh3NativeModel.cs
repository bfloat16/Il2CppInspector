using Il2CppInspector.Cpp;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model;

internal sealed class Bh3NativeModel(TypeModel model) : MoraxNativeModel(model)
{
    // Il2CppClass is 0xD8 bytes with the vtable pointer at +0x00 and 16-byte slots (pool_alloc(2 * vtableCount, 8):
    // method pointer + MethodInfo*), name +0x20, properties +0x28, typeDefinition +0x38, fields +0x50, image +0x58,
    // interface offsets +0x70, events +0x78, byval_arg / this_arg +0x88 / +0x98.
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
                            var name = VTableSlotName(definition, slot);
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
