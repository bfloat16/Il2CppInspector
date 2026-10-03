namespace Il2CppInspector.Tests.Common.IL2CPP
{
    internal static class LoadingTests
    {
        internal static Inspector LoadStock(string[] args)
        {
            var stock = args[1];
            var package = Inspector.LoadFromFile(Path.Combine(stock, "GameAssembly.dll"), Path.Combine(stock, "2019440f1_Data", "il2cpp_data", "Metadata", "global-metadata.dat"))?.Single();
            Check(package != null && !package.Metadata.IsZenlessZoneZero, "Stock metadata keeps its normal loader");
            return package;
        }
    }
}
