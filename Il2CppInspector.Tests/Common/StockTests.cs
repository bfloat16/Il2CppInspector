namespace Il2CppInspector.Tests.Common
{
    internal static class StockTests
    {
        internal static void Run(string[] args)
        {
            var package = IL2CPP.LoadingTests.LoadStock(args);
            var model = new TypeModel(package);
            Model.AppModelTests.Run(model);
            Cpp.CppTypeTests.Run();
            Reflection.TypeModelTests.VerifyGenericEnums(model);
        }
    }
}
