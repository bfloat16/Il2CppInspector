namespace Il2CppInspector.Tests.Plugin.ZZZ.Outputs;

internal static class ZzzPythonScriptTests
{
    internal static void VerifyDebugSupplement(AppModel app, string output)
    {
        Directory.CreateDirectory(output);
        Check(DebugOutput.SelectFormat(app.Package.BinaryImage) == DebugSymbolFormat.Pdb, "Debug automatically routes the actual ZZZ PE image to PDB");
        var json = Path.Combine(output, "supplement.json");
        var methodsBefore = app.TypeModel.ResolvedGenericMethods.Count;
        new JSONMetadata(app) { SupplementDebugInfo = true }.Write(json);
        using (var input = File.OpenRead(json))
        using (var document = JsonDocument.Parse(input))
        {
            var map = document.RootElement.GetProperty("addressMap");
            Check(
                !map.EnumerateObject().Any(p => JSONMetadata.IsDebugSymbolSection(p.Name)),
                "Supplementary JSON omits function boundaries, definitions, prototypes, invokers, APIs and unused symbol tables"
            );
            foreach (var name in new[] { "stringLiterals", "typeInfoPointers", "typeRefPointers", "methodInfoPointers", "typeMetadata", "arrayMetadata", "fields", "fieldRvas" })
                Check(map.GetProperty(name).ValueKind == JsonValueKind.Array, $"Supplementary JSON retains {name}");
            Check(
                map.GetProperty("stringLiterals").GetArrayLength() > 0 && map.GetProperty("methodInfoPointers").GetArrayLength() > 0,
                "Supplementary JSON retains real strings and MethodInfo references"
            );
            if (app.Package.Metadata.HasGameAdapter)
                Check(map.GetProperty("moraxRuntimeCaches").GetArrayLength() > 0, "Supplementary JSON retains game-specific runtime caches");
        }
        var header = Path.Combine(output, "must-not-exist.h");
        foreach (var target in PythonScript.GetAvailableTargets())
        {
            var script = Path.Combine(output, $"supplement-{target}.py");
            new PythonScript(app).WriteScriptToFile(script, target, header, json, supplementDebugInfo: true);
            var text = File.ReadAllText(script);
            Check(!File.Exists(header) && !File.Exists(Path.ChangeExtension(script, ".h")), $"Supplementary {target} generation never emits a header");
            Check(
                text.Contains("supplement_debug_info: bool = True") && !text.Contains("must-not-exist.h"),
                $"Generated supplementary {target} script loads no header and skips debug-owned functions"
            );
        }
        Check(
            app.TypeModel.ResolvedGenericMethods.Count == methodsBefore && app.AnalysisMethods.Count == 0 && app.AnalysisTypes.Count == 0,
            "Supplementary output does not build native method or type graphs or materialize constructed methods"
        );
        var support = app.EnumerateNativeSupportMethods().ToArray();
        var registrationSignature = app.GetRegistrationSignature();
        Check(
            registrationSignature.Name == "Morax_MetadataCache_Register" && registrationSignature.ReturnType.Name == "void *" && registrationSignature.Arguments.Count == 0,
            "Active ZZZ adapter retains its custom registration declaration"
        );
        foreach (var api in app.AvailableAPIs)
            if (!support.Any(m => m.Name == api.Key && m.Address == app.AvailableAPIs.primaryToSubkeyMapping[api.Key] && m.Signature == api.Value))
                throw new InvalidOperationException($"Native debug output does not cover API {api.Key}.");
        var registration = support.Single(m => m.Address == app.Package.Binary.RegistrationFunctionPointer && m.Name == "Morax_MetadataCache_Register");
        Check(
            registration.Signature != null && registration.SignatureComplete && registration.Signature.Arguments.Count == 0,
            "Native debug symbols cover the game's registration function and APIs omitted from the supplement"
        );
        Console.WriteLine($"Supplementary JSON: {new FileInfo(json).Length / 1048576.0:F1} MiB");
    }
}
