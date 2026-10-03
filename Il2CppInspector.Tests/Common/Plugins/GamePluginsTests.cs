namespace Il2CppInspector.Tests.Common.Plugins
{
    internal static class GamePluginsTests
    {
        internal static void Run(string[] unsupportedIds)
        {
            foreach (var unsupported in unsupportedIds)
            {
                var rejected = false;
                try
                {
                    GamePlugins.Get(unsupported);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                {
                    rejected = true;
                }
                Check(rejected, "Unsupported or malformed game ID is rejected: " + unsupported);
            }
        }
    }
}
