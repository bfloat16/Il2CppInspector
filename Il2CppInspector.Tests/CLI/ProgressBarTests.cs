using Il2CppInspector.CLI;

namespace Il2CppInspector.Tests.CLI;

internal static class ProgressBarTests
{
    internal static void Run(string[] args)
    {
        var half = new OperationProgress("Building type model", 50, 100, "Type references");
        Check(ProgressBar.Render(half with { Current = 0, Total = 0 }, TimeSpan.Zero).Contains("100%"), "Empty work completes without division by zero");
        var unknown = half with { Current = 0, Total = -1 };
        Check(ProgressBar.Render(unknown, TimeSpan.Zero).Contains("--%"), "Unknown work does not invent a percentage");
        Check(ProgressBar.Render(unknown, TimeSpan.Zero) != ProgressBar.Render(unknown, TimeSpan.FromMilliseconds(100)), "Unknown work animates inside the same bar");
        Check(
            new[] { 10, 40, 80, 120 }.All(width => ProgressBar.Render(half with { Label = new string('a', 300) }, TimeSpan.Zero, width).Length <= width),
            "Long progress labels stay inside the terminal width"
        );

        TextWriter original = Console.Out;
        using var output = new StringWriter();
        string success;
        string failure;
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
            try
            {
                ProgressBar.Run("Building type model", _ => throw new InvalidOperationException("fixture"));
                throw new Exception("The fixture exception was swallowed");
            }
            catch (InvalidOperationException ex) when (ex.Message == "fixture") { }
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
        Check(failure.Contains("failed") && !failure.Contains("100%") && !failure.Contains("done"), "Failed operations propagate exceptions and do not claim completion");
        Check(output.ToString().Contains("100%") && output.ToString().Contains("Processed 2000 relocations"), "Relocation progress finishes before the next status message");
        if (Console.IsOutputRedirected)
        {
            Check(
                !success.Replace(Environment.NewLine, "\n").Contains('\r') && success.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length <= 13,
                "Redirected logs use bounded milestones without carriage-return refreshes"
            );
        }

        if (args.Length == 1)
            return;
        if (args.Length != 4)
            throw new ArgumentException("--progress accepts a binary, metadata file and output directory");
        var relocations = new List<OperationProgress>();
        var package = Inspector.LoadFromFile(args[1], args[2], new LoadOptions { ProgressCallback = relocations.Add })?.Single() ?? throw new InvalidDataException("Failed to load progress fixture");
        Verify(relocations, "Relocations");
        var types = new List<OperationProgress>();
        var model = new TypeModel(package, types.Add);
        Verify(types, "Type model");
        var assemblies = new List<OperationProgress>();
        new AssemblyShims(model) { ProgressCallback = assemblies.Add }.Write(args[3]);
        Verify(assemblies, "DummyDll");
        long expected = model.Assemblies.Count * (package.Metadata.HasGameAdapter ? 1L : 3L);
        Check(assemblies[^1].Total == expected, "DummyDll progress accounts for stock and plugin stage counts");
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
