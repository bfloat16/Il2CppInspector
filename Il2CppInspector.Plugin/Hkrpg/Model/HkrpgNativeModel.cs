using Il2CppInspector.Cpp;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model;

internal sealed class HkrpgNativeModel : NativeTypeModel
{
    private HkrpgMorax Adapter => (HkrpgMorax)Package.Metadata.GameAdapter;
    public override Dictionary<TypeInfo, int> GenericOrdinals { get; }
    public override Dictionary<int, TypeInfo> GenericTypes { get; }

    internal HkrpgNativeModel(TypeModel model) : base(model)
    {
        var layouts = (SharedTypeLayouts)model.GameLayouts;
        GenericOrdinals = layouts.GenericOrdinals;
        GenericTypes = layouts.GenericTypes;
    }

    internal override int Alignment(TypeInfo type) => Adapter.LayoutAlignment(Adapter.DefinitionLayoutGroup(type.Index));

    internal override CppComplexType CreateClass(NativeLayoutAnalysisModel layouts, TypeInfo type) => layouts.Declare(Name(type) + "__Class", -1, _ => { });
}
