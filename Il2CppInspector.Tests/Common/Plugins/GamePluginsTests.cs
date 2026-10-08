namespace Il2CppInspector.Tests.Common.Plugins
{
    internal static class GamePluginsTests
    {
        internal static void Run(string[] unsupportedIds)
        {
            var availableGames = $"Available games: {string.Join(", ", GamePlugins.Installed.Select(p => p.GameId))}";
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
                    Check(ex.Message.Contains(availableGames), "Invalid game ID lists all installed games: " + unsupported);
                    if (ex is ArgumentException)
                        Check(ex.Message == availableGames, "Malformed game ID directly lists available games: " + unsupported);
                }
                Check(rejected, "Unsupported or malformed game ID is rejected: " + unsupported);
            }
        }
    }
}
