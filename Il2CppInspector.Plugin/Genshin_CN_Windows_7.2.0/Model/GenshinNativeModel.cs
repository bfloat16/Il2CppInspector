using Il2CppInspector.Cpp;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model;

// Only recovered MORAX descriptor and class/vtable layout differences belong here.
internal sealed class GenshinNativeModel : MoraxNativeModel
{
    private GenshinMorax Adapter => (GenshinMorax)Package.Metadata.GameAdapter;

    internal GenshinNativeModel(TypeModel model)
        : base(model) { }

    internal override CppComplexType CreateClass(NativeLayoutAnalysisModel layouts, TypeInfo type)
    {
        var definition = type.Definition.IsValid ? type.Definition : type.GetGenericTypeDefinition().Definition;
        var split = type.IsGenericType && !type.IsGenericTypeDefinition || Adapter.DefinitionHasSplitVTable(definition);
        var extra = Adapter.DefinitionExtraVTableSlots((int)(definition.Token & 0xFFFFFF));
        return DeclareInlineVTableClass(layouts, type, split, extra);
    }
}
