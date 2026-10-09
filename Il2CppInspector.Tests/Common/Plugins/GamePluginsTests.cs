namespace Il2CppInspector.Tests.Common.Plugins
{
    internal static class GamePluginsTests
    {
        internal static void Run(string[] unsupportedIds)
        {
            foreach (var plugin in GamePlugins.Installed)
            {
                var parts = plugin.GameId.Split('_');
                Check(parts.Length == 4 && (parts[3] == "x.x.x" || Version.TryParse(parts[3], out _)), "Installed game ID includes name, region, platform and version: " + plugin.GameId);
                Check(ReferenceEquals(GamePlugins.Get(plugin.GameId.ToLowerInvariant()), plugin), "Game selection remains case-insensitive: " + plugin.GameId);
            }
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
