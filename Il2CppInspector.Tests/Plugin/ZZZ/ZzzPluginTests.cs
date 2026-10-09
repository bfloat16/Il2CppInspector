namespace Il2CppInspector.Tests.Plugin.ZZZ
{
    internal static class ZzzPluginTests
    {
        internal static void Run()
        {
            var plugin = GamePlugins.Get("ZZZ_CN_Windows_3.2.0");
            Common.Plugins.GamePluginsTests.Run(["ZZZ_CN_Windows_3.2.1", "ZZZ_OS_Windows_3.2.0", "ZZZ", "ZZZ_CN_Windows_bad", "ZZZ_CN_3.2.0", "ZZZ_CN_Android_3.2.0"]);
            var stderr = Console.Error;
            using var errors = new StringWriter();
            try
            {
                Console.SetError(errors);
                using var binary = new MemoryStream();
                using var metadata = new MemoryStream("stock"u8.ToArray());
                Check(
                    Inspector.LoadFromStream(binary, metadata, new LoadOptions { Game = plugin.GameId }) == null && errors.ToString().Contains("does not match"),
                    "Explicit game selection rejects mismatched metadata"
                );
                errors.GetStringBuilder().Clear();
                using var morax = new MemoryStream("MHY\0"u8.ToArray());
                Check(
                    Inspector.LoadFromStream(binary, morax, new LoadOptions { Game = plugin.GameId }) == null && errors.ToString().Contains("requires startup-metadata.dat"),
                    "Missing startup metadata is reported by the plugin"
                );
            }
            finally
            {
                Console.SetError(stderr);
            }
        }
    }
}
