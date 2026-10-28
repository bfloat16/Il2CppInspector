using Il2CppInspector.Cpp;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model;

internal sealed class ZzzNativeModel : MoraxNativeModel
{
    private ZzzMorax Adapter => (ZzzMorax)Package.Metadata.GameAdapter;

    internal ZzzNativeModel(TypeModel model)
        : base(model) { }

    internal override CppComplexType CreateClass(NativeLayoutAnalysisModel layouts, TypeInfo type)
    {
        var definition = type.Definition.IsValid ? type.Definition : type.GetGenericTypeDefinition().Definition;
        var split = type.IsGenericType && !type.IsGenericTypeDefinition || Adapter.DefinitionHasSplitVTable(definition);
        return DeclareInlineVTableClass(layouts, type, split, 0);
    }
}
