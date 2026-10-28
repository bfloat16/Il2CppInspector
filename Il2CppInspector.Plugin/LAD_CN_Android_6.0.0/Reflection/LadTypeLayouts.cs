using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.LAD
{
    /// <summary>
    /// Constructed generic layouts have no recorded bindings; static layout inference is not implemented.
    /// </summary>
    internal sealed class LadTypeLayouts : Plugins.IGameTypeLayouts
    {
        public void Bind(TypeInfo type) { }
    }
}
