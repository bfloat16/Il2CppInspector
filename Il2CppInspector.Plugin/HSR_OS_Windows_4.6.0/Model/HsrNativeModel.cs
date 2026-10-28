using Il2CppInspector.Cpp;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model;

internal sealed class HsrNativeModel(TypeModel model) : MoraxNativeModel(model)
{
    internal override CppComplexType CreateClass(NativeLayoutAnalysisModel layouts, TypeInfo type) =>
        layouts.Declare(Name(type) + "__Class", 0xC0, node => NativeLayoutAnalysisModel.Add(node, "header", layouts.Cpp.GetType("Il2CppClass_0"), 0));
}
