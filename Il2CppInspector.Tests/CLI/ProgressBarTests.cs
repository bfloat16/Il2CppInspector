using Il2CppInspector.CLI;

namespace Il2CppInspector.Tests.CLI;

internal static class ProgressBarTests
{
    internal static void Run(string[] args)
    {
        TextWriter original = Console.Out;
        using var output = new StringWriter();
        string success;
        string failure;
        bool propagated;
        try
        {
            Console.SetOut(output);
            ProgressBar.Run(
                "Generating DummyDlls",
                report =>
                {
                    for (int i = 0; i < 100; i++)
                        report(new OperationProgress("Generating DummyDlls", i, 100));
                }
            );
            success = output.ToString();
            output.GetStringBuilder().Clear();
            propagated = Throws<InvalidOperationException>(() =>
                ProgressBar.Run("Building type model", _ => throw new InvalidOperationException("fixture"))
            );
            failure = output.ToString();
            output.GetStringBuilder().Clear();
            ProgressBar.Update(new OperationProgress("Processing relocations", 1000, 2000));
            ProgressBar.Update(new OperationProgress("Processing relocations", 2000, 2000));
            ProgressBar.WriteStatus("Processed 2000 relocations");
        }
        finally
        {
            Console.SetOut(original);
        }
        Check(success.Contains("100%") && success.Contains("done"), "Successful operations finish at 100 percent");
        Check(
            propagated && failure.Contains("failed") && !failure.Contains("100%") && !failure.Contains("done"),
            "Failed operations propagate exceptions and do not claim completion"
        );
        Check(
            output.ToString().Contains("100%") && output.ToString().Contains("Processed 2000 relocations"),
            "Relocation progress finishes before the next status message"
        );
        if (Console.IsOutputRedirected)
        {
            Check(
                !success.Replace(Environment.NewLine, "\n").Contains('\r') && success.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length <= 13,
                "Redirected logs use bounded milestones without carriage-return refreshes"
            );
        }

        if (args.Length == 1)
            return;
        var relocations = new List<OperationProgress>();
        var package = Inspector.LoadFromFile(args[1], args[2], new LoadOptions { ProgressCallback = relocations.Add }).Single();
        Verify(relocations, "Relocations");
        var types = new List<OperationProgress>();
        var model = new TypeModel(package, types.Add);
        Verify(types, "Type model");
        var assemblies = new List<OperationProgress>();
        new AssemblyShims(model) { ProgressCallback = assemblies.Add }.Write(args[3]);
        Verify(assemblies, "DummyDll");
    }

    private static void Verify(List<OperationProgress> reports, string label)
    {
        if (label == "Relocations" && reports.Count == 0)
            return;
        Check(reports.Count > 0 && reports[0].Current == 0, $"{label} starts at zero");
        Check(reports.All(p => p.Current >= 0 && p.Current <= p.Total), $"{label} stays within its total");
        Check(reports.Zip(reports.Skip(1)).All(pair => pair.First.Current <= pair.Second.Current), $"{label} updates monotonically");
        Check(reports[^1].Current == reports[^1].Total, $"{label} reports all completed work");
    }
}
