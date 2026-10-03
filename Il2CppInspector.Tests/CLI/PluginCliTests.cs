namespace Il2CppInspector.Tests.CLI
{
    internal static class PluginCliTests
    {
        internal static void Run(string[] args)
        {
            var cli = Path.GetFullPath(args[1]);
            Check(RunCli(cli, "ZZZ_CN_3.2.1").Contains("Available games: ZZZ_CN_3.2.0"), "Published CLI discovers game DLLs through the plugin loader library");
            var isolated = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(isolated);
            foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(cli)))
            {
                if (!Path.GetFileName(file).StartsWith("Il2CppInspector.Plugin.", StringComparison.Ordinal))
                {
                    File.Copy(file, Path.Combine(isolated, Path.GetFileName(file)), true);
                }
            }
            var missing = RunCli(Path.Combine(isolated, Path.GetFileName(cli)), "ZZZ_CN_3.2.0");
            Check(missing.Contains("No installed plugin supports ZZZ_CN_3.2.0") && !missing.Contains("Available games: ZZZ_CN_3.2.0"), "CLI without game DLLs reports the missing plugin clearly");

            static string RunCli(string cli, string gameId)
            {
                var info = new System.Diagnostics.ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var argument in new[] { cli, "--game", gameId, "-i", "unused.bin", "-m", "unused.dat" })
                {
                    info.ArgumentList.Add(argument);
                }
                using var process = System.Diagnostics.Process.Start(info);
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                Check(process.ExitCode == 1, "CLI rejects an unsupported game before opening input files");
                return stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            }
        }
    }
}
