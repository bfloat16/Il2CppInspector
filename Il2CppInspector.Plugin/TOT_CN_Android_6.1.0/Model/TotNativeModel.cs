using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.TOT
{
    /// <summary>
    /// Minimal native-model implementation for TOT. The game uses the stock IL2CPP naming layout, so
    /// type names map directly to C identifiers and no runtime-specific storage naming is required.
    /// </summary>
    internal sealed class TotNativeModel : Plugins.GameNativeModel
    {
        private readonly TypeModel model;

        public TotNativeModel(TypeModel model) => this.model = model;

        public override Dictionary<TypeInfo, int> GenericOrdinals { get; } = [];
        public override Dictionary<int, TypeInfo> GenericTypes { get; } = [];
        public override Dictionary<string, TypeInfo> ArrayTypes { get; } = [];

        public override string Name(TypeInfo type) => type == null ? "void" : type.Name.ToCIdentifier();

        public override string ClassName(TypeInfo type) => Name(type) + "__Class";

        public override string CType(TypeInfo type)
        {
            if (type == null)
            {
                return "void";
            }

            if (type.IsPointer || type.IsByRef)
            {
                return "void *";
            }

            return type.IsValueType ? "struct " + Name(type) : "void *";
        }

        public override bool IsSignatureTypeComplete(TypeInfo type) => type != null && (type.IsPrimitive || model.Types.Contains(type) || type.IsArray);

        public override string ArrayName(TypeInfo type) => type == null || !type.IsArray ? null : Name(type) + "_Array";
    }
}
