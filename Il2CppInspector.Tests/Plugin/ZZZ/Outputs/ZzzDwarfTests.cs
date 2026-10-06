namespace Il2CppInspector.Tests.Plugin.ZZZ.Outputs;

internal static class ZzzDwarfTests
{
    internal static void Run(ZzzTestContext context, string output)
    {
        Directory.CreateDirectory(output);
        var model = context.Model;
        var before = model.ResolvedGenericMethods.Count;
        var app = new AppModel(model, false).Build(new UnityVersion("2019.4.40f1"));
        var image = context.Input.BinaryImage;
        var position = image.Position;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = new DwarfOutput(app).Write(Path.Combine(output, "GameAssembly.sym"), (_, message) => Console.WriteLine(message));
        Console.WriteLine($"DWARF: {timer.Elapsed.TotalSeconds:F1}s; managed heap {GC.GetTotalMemory(false) / 1048576} MiB");
        Check(result.Functions > 800000 && result.TypedFunctions > 800000 && result.TypeRecords > 100000, "ZZZ DWARF exports functions and typed layouts directly from the analysis pipeline");
        Check(model.ResolvedGenericMethods.Count == before, "DWARF generation keeps constructed method objects lazy");
        Check(image.Position == position, "DWARF output preserves the input stream position");
        var vector = app.RuntimeCppTypes.GetComplexType(app.GameNativeModel.Name(context.Vector));
        Check(
            vector.SizeBytes == 12 && vector["x"].OffsetBytes == 0 && vector["y"].OffsetBytes == 4 && vector["z"].OffsetBytes == 8,
            "Native layouts rebuild unchanged after DWARF releases transient fields"
        );
    }
}
