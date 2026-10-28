using Il2CppInspector.Cpp;
using Il2CppInspector.Model;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.LAD;

internal sealed class LadNativeModel(TypeModel model) : NativeTypeModel(model)
{
    // All 44759 cached_class pointers are zero at 0xA284B90-0xA3968B0. Static generic layout inference is not implemented.
    public override Dictionary<TypeInfo, int> GenericOrdinals { get; } = [];
    public override Dictionary<int, TypeInfo> GenericTypes { get; } = [];

    internal override int Alignment(TypeInfo type)
    {
        if (!type.IsValueType || type.HasElementType)
            return 8;
        if (type.IsEnum)
            return Alignment(type.GetEnumUnderlyingType());
        if (type.IsPrimitive)
            return Math.Max(1, Math.Min(8, checked((int)type.Sizes.InstanceSize - 16)));
        var alignment = type.DeclaredFields.Where(static f => !f.IsStatic).Select(f => Alignment(f.FieldType)).DefaultIfEmpty(1).Max();
        var packing = (int)type.Definition.Bitfield.PackingSize;
        return packing == 0 ? alignment : Math.Min(alignment, 1 << (packing - 1));
    }

    internal override CppComplexType CreateClass(NativeLayoutAnalysisModel layouts, TypeInfo type)
    {
        var cpp = layouts.Cpp;
        var count = type.Definition.VTableCount;
        // 0x2471EF0 allocates the 0x120-byte prefix followed by VirtualInvokeData[count].
        return layouts.Declare(
            ClassName(type),
            0x120 + count * 16,
            node =>
            {
                NativeLayoutAnalysisModel.Add(node, "_0", cpp.GetType("Il2CppClass_0"), 0);
                NativeLayoutAnalysisModel.Add(node, "interfaceOffsets", cpp.GetType("Il2CppRuntimeInterfaceOffsetPair *"), 0xA8);
                NativeLayoutAnalysisModel.Add(node, "static_fields", cpp.GetType("void *"), 0xB0);
                NativeLayoutAnalysisModel.Add(node, "rgctx_data", cpp.GetType("Il2CppRGCTXData *"), 0xB8);
                NativeLayoutAnalysisModel.Add(node, "_1", cpp.GetType("Il2CppClass_1"), 0xC0);
                if (count > 0)
                    NativeLayoutAnalysisModel.Add(node, "vtable", layouts.Cpp.GetType("VirtualInvokeData").AsArray(count), 0x120);
            }
        );
    }
}
