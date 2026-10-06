namespace Il2CppInspector.Tests.CLI;

internal static class OutputTargetTests
{
    internal static void Run(string cli)
    {
        Check(RunCli(["-t", "Debug"]).Contains("Binary file not found"), "CLI accepts Debug as a standalone output target");
        Check(
            RunCli(["-t", "IDA", "-t", "Ghidra", "-t", "BinaryNinja", "-t", "Debug", "-t", "debug"]).Contains("Binary file not found"),
            "CLI accepts all disassemblers with duplicate case-insensitive Debug targets"
        );
        Check(RunCli(["-t", "missing", "-t", "Debug"]).Contains("Unknown output target: missing"), "A later target cannot hide an earlier invalid target");
        Check(RunCli(["-t", "IDA", "-t"]).Contains("requires a value"), "A repeated target still requires a value");
        Check(RunCli(["--script-target", "IDA", "--target", "Debug"]).Contains("Binary file not found"), "Both long target option names remain usable");
        foreach (var oldTarget in new[] { "PDB", "DWARF" })
            Check(RunCli(["-t", oldTarget, "-t", "Debug"]).Contains("Unknown output target: " + oldTarget), $"CLI rejects removed target {oldTarget} even when Debug follows");

        string RunCli(string[] targets)
        {
            var start = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { Path.GetFullPath(cli), "-i", "missing.bin", "-m", "missing.dat" }.Concat(targets))
                start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 1)
                throw new InvalidOperationException($"CLI returned unexpected exit code {process.ExitCode}.");
            return stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        }
    }
}
