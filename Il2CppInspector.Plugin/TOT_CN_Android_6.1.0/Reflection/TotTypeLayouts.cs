using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.TOT
{
    /// <summary>
    /// TOT uses the stock IL2CPP v24.1 field/generic layout, so no special layout-group binding is
    /// required. The interface is still implemented so the plugin has a concrete layout provider.
    /// </summary>
    internal sealed class TotTypeLayouts : Plugins.IGameTypeLayouts
    {
        public void Bind(TypeInfo type)
        {
            // Stock layout: nothing to remap.
        }
    }
}
