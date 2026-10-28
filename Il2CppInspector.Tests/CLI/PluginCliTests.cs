namespace Il2CppInspector.Tests.CLI
{
    internal static class PluginCliTests
    {
        private const string MissingGame = "NONE_CN_Windows_0.0.0";

        internal static void Run(string[] args)
        {
            var cli = Path.GetFullPath(args[1]);
            var available = RunCli(cli);
            Check(
                GamePlugins.Installed.All(p => available.Contains(p.GameId)),
                "Published CLI discovers every game DLL through the plugin loader library"
            );
            var isolated = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(isolated);
            foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(cli)))
            {
                if (!Path.GetFileName(file).StartsWith("Il2CppInspector.Plugin.", StringComparison.Ordinal))
                {
                    File.Copy(file, Path.Combine(isolated, Path.GetFileName(file)), true);
                }
            }
            var missing = RunCli(Path.Combine(isolated, Path.GetFileName(cli)));
            Check(
                missing.Contains("No installed plugin supports " + MissingGame)
                    && missing.TrimEnd().EndsWith("Available games:", StringComparison.Ordinal),
                "CLI without game DLLs reports the missing plugin clearly"
            );

            static string RunCli(string cli)
            {
                var (exitCode, output, errors) = TestEnvironment.RunProcess(
                    "dotnet",
                    [cli, "--game", MissingGame, "-i", "unused.bin", "-m", "unused.dat"]
                );
                if (exitCode != 1)
                    throw new InvalidOperationException($"CLI returned unexpected exit code {exitCode}.");
                return output + errors;
            }
        }
    }
}
