using System.Reflection;
using McMaster.NETCore.Plugins;

namespace Il2CppInspector.Plugins
{
    public static class GamePlugins
    {
        private static readonly Lazy<IReadOnlyList<GamePlugin>> plugins = new(Discover);
        private static readonly List<PluginLoader> loaders = [];
        public static IReadOnlyList<GamePlugin> Installed => plugins.Value;

        public static GamePlugin Get(string gameId)
        {
            var parts = gameId?.Split('_');
            if (parts == null || (parts.Length != 2 && parts.Length != 3) || parts.Any(string.IsNullOrWhiteSpace) || (parts.Length == 3 && !Version.TryParse(parts[2], out _)))
            {
                throw new ArgumentException($"Available games: {string.Join(", ", Installed.Select(p => p.GameId))}");
            }

            return Installed.SingleOrDefault(p => string.Equals(p.GameId, gameId, StringComparison.OrdinalIgnoreCase))
                ?? throw new NotSupportedException($"No installed plugin supports {gameId}. Available games: {string.Join(", ", Installed.Select(p => p.GameId))}");
        }

        internal static GamePlugin Select(string gameId, ReadOnlySpan<byte> metadata)
        {
            if (gameId != null)
            {
                var selected = Get(gameId);
                if (!selected.Matches(metadata))
                {
                    throw new InvalidDataException($"Metadata does not match game plugin {selected.GameId}.");
                }

                return selected;
            }
            GamePlugin match = null;
            foreach (var plugin in Installed)
            {
                if (!plugin.Matches(metadata))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException("Multiple game plugins match this metadata; specify --game with an installed game ID.");
                }

                match = plugin;
            }
            return match;
        }

        private static IReadOnlyList<GamePlugin> Discover()
        {
            var assemblies = new HashSet<Assembly>(
                AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name.StartsWith("Il2CppInspector.Plugin.", StringComparison.Ordinal))
            );
            foreach (var directory in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "plugins") })
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                var search = directory == AppContext.BaseDirectory ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
                foreach (var path in Directory.EnumerateFiles(directory, "Il2CppInspector.Plugin.*.dll", search).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var name = AssemblyName.GetAssemblyName(path);
                    var assembly = assemblies.FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), name));
                    if (assembly == null)
                    {
                        var loader = PluginLoader.CreateFromAssemblyFile(Path.GetFullPath(path), sharedTypes: [typeof(GamePlugin)], configure: config => config.PreferSharedTypes = true);
                        loaders.Add(loader);
                        assembly = loader.LoadDefaultAssembly();
                    }
                    assemblies.Add(assembly);
                }
            }
            var result = assemblies
                .SelectMany(a => a.GetExportedTypes())
                .Where(t => !t.IsAbstract && typeof(GamePlugin).IsAssignableFrom(t))
                .Select(t => (GamePlugin)Activator.CreateInstance(t))
                .OrderBy(p => p.GameId, StringComparer.Ordinal)
                .ToArray();
            if (result.GroupBy(p => p.GameId, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            {
                throw new InvalidOperationException("Duplicate game plugin IDs are installed.");
            }

            return result;
        }
    }
}
