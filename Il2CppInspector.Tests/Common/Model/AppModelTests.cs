namespace Il2CppInspector.Tests.Common.Model
{
    internal static class AppModelTests
    {
        internal static void Run(TypeModel stockModel)
        {
            var stockApp = new AppModel(stockModel, false).Build(new UnityVersion("2019.4.21f1"));
            Check(stockApp.GetVTableIndexFromClassOffset(stockApp.GetVTableOffset() + 16) == 1, "Stock virtual-call slot lookup keeps its sixteen-byte AoS stride");
        }
    }
}
