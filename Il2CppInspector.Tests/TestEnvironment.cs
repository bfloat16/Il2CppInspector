namespace Il2CppInspector.Tests
{
    internal static class TestEnvironment
    {
        // Source fixtures are read from the repository rather than copied to the build output.
        internal static string RepositoryPath(params string[] parts)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "Il2CppInspector.slnx")))
                root = root.Parent;
            return Path.Combine(parts.Prepend(root.FullName).ToArray());
        }

        internal static string FixturePath(string name) => RepositoryPath("Il2CppInspector.Tests", "Common", "Outputs", "Fixtures", name);

        internal static (int ExitCode, string Output, string Errors) RunProcess(string executable, IEnumerable<string> arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start);
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, output.GetAwaiter().GetResult(), errors.GetAwaiter().GetResult());
        }

        internal static void RunTool(string executable, IEnumerable<string> arguments)
        {
            var (exitCode, output, errors) = RunProcess(executable, arguments);
            if (exitCode != 0)
                throw new InvalidOperationException($"{executable} failed: {output} {errors}");
        }
    }
}
