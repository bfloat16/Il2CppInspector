using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugins
{
    public abstract class GameNativeModel
    {
        public abstract Dictionary<TypeInfo, int> GenericOrdinals { get; }
        public abstract Dictionary<int, TypeInfo> GenericTypes { get; }
        public abstract Dictionary<string, TypeInfo> ArrayTypes { get; }

        public abstract string Name(TypeInfo type);
        public abstract string ClassName(TypeInfo type);
        public abstract string CType(TypeInfo type);
        public abstract bool IsSignatureTypeComplete(TypeInfo type);
        public abstract string ArrayName(TypeInfo type);
    }
}
