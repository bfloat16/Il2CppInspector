namespace Il2CppInspector.Plugins
{
    public interface IGameAnalysisModel
    {
        void BuildMethods();
        void BuildTypes();
        void BuildOrderedTypes();
        IEnumerable<Model.NativeMethod> EnumerateNativeMethods();
        IEnumerable<Cpp.CppType> EnumerateNativeTypes();
    }
}
