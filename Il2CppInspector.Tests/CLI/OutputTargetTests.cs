namespace Il2CppInspector.Tests.CLI;

internal static class OutputTargetTests
{
    internal static void Run(string cli)
    {
        Check(RunCli(["-t", "IDA", "-t", "PDB"]).Contains("Binary file not found"), "CLI accepts repeated IDA and PDB targets");
        Check(RunCli(["-t", "IDA", "-t", "Ghidra", "-t", "PDB", "-t", "pdb"]).Contains("Binary file not found"), "CLI accepts multiple script targets and duplicate case-insensitive targets");
        Check(RunCli(["-t", "missing", "-t", "PDB"]).Contains("Unknown output target: missing"), "A later target cannot hide an earlier invalid target");
        Check(RunCli(["-t", "IDA", "-t"]).Contains("requires a value"), "A repeated target still requires a value");
        Check(RunCli(["--script-target", "IDA", "--target", "PDB"]).Contains("Binary file not found"), "Both long target option names remain usable");

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
            Check(process.ExitCode == 1, "CLI reports argument or input failures with exit code 1");
            return stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        }
    }
}
