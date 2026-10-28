namespace Il2CppInspector.Tests.Common.Plugins
{
    internal static class GamePluginsTests
    {
        internal static void Run()
        {
            foreach (var plugin in GamePlugins.Installed)
            {
                Check(
                    ReferenceEquals(GamePlugins.Get(plugin.GameId.ToLowerInvariant()), plugin),
                    "Game selection remains case-insensitive: " + plugin.GameId
                );
                Check(!plugin.Matches([]) && !plugin.Matches("stock"u8), "Plugin rejects truncated and foreign metadata: " + plugin.GameId);
            }
            Plugin.GameSampleTests.RunInstalled();
        }
    }
}
