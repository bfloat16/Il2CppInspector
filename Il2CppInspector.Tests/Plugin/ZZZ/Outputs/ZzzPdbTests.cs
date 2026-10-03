namespace Il2CppInspector.Tests.Plugin.ZZZ.Outputs;

internal static class ZzzPdbTests
{
    internal static void Run(ZzzTestContext context, string output)
    {
        Directory.CreateDirectory(output);
        var model = context.Model;
        var before = model.ResolvedGenericMethods.Count;
        var app = new AppModel(model, false).Build(new UnityVersion("2019.4.40f1"));
        var result = new PdbOutput(app).Write(Path.Combine(output, "GameAssembly.pdb"), (_, message) => Console.WriteLine(message));
        Check(result.Functions > 800000 && result.TypedFunctions > 800000 && result.TypeRecords > 100000, "ZZZ PDB exports native functions and typed layouts directly from the analysis pipeline");
        Check(model.ResolvedGenericMethods.Count == before, "PDB generation keeps constructed method objects lazy");
        var vector = app.RuntimeCppTypes.GetComplexType(app.GameNativeModel.Name(context.Vector));
        Check(
            vector.SizeBytes == 12 && vector["x"].OffsetBytes == 0 && vector["y"].OffsetBytes == 4 && vector["z"].OffsetBytes == 8,
            "Native layouts rebuild with identical sizes and offsets after PDB releases transient fields"
        );
    }

    internal static void Profile(ZzzTestContext context, string output, bool withPdb)
    {
        Directory.CreateDirectory(output);
        var app = new AppModel(context.Model, false).Build(new UnityVersion("2019.4.40f1"));
        Timed("Header", () => new CppScaffolding(app, useBetterArraySize: true).WriteTypes(Path.Combine(output, "il2cpp.h")));
        Timed("JSON", () => new JSONMetadata(app).Write(Path.Combine(output, "il2cpp.json")));
        if (withPdb)
            Timed("PDB", () => new PdbOutput(app).Write(Path.Combine(output, "GameAssembly.pdb")));

        static void Timed(string stage, Action action)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            action();
            Console.WriteLine($"PROFILE {stage}: {timer.Elapsed.TotalSeconds:F1}s; managed heap {GC.GetTotalMemory(false) / 1048576} MiB");
        }
    }
}
