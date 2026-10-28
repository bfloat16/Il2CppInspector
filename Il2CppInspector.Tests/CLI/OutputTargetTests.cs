namespace Il2CppInspector.Tests.CLI;

internal static class OutputTargetTests
{
    internal static void Run(string cli)
    {
        Check(
            RunCli(["-t", "IDA", "-t", "Ghidra", "-t", "BinaryNinja", "-t", "Debug", "-t", "debug"]).Contains("Binary file not found"),
            "CLI accepts all disassemblers with duplicate case-insensitive Debug targets"
        );
        Check(
            RunCli(["-t", "missing", "-t", "Debug"]).Contains("Unknown output target: missing"),
            "A later target cannot hide an earlier invalid target"
        );

        string RunCli(string[] targets)
        {
            var (exitCode, output, errors) = TestEnvironment.RunProcess(
                "dotnet",
                new[] { Path.GetFullPath(cli), "-i", "missing.bin", "-m", "missing.dat" }.Concat(targets)
            );
            if (exitCode != 1)
                throw new InvalidOperationException($"CLI returned unexpected exit code {exitCode}.");
            return output + errors;
        }
    }
}
