namespace Il2CppInspector.Tests.Plugin.ZZZ.Outputs;

internal static class ZzzPythonScriptTests
{
    internal static void VerifyDebugSupplement(AppModel app, string output)
    {
        Directory.CreateDirectory(output);
        Check(DebugOutput.SelectFormat(app.Package.BinaryImage) == DebugSymbolFormat.Pdb, "Debug automatically routes the actual ZZZ PE image to PDB");
        var json = Path.Combine(output, "supplement.json");
        new JSONMetadata(app) { SupplementDebugInfo = true }.Write(json);
        using (var input = File.OpenRead(json))
        using (var document = JsonDocument.Parse(input))
        {
            var map = document.RootElement.GetProperty("addressMap");
            foreach (
                var name in new[]
                {
                    "methodDefinitions",
                    "constructedGenericMethods",
                    "customAttributesGenerators",
                    "methodInvokers",
                    "functionAddresses",
                    "functionMetadata",
                    "apis",
                    "exports",
                    "symbols",
                }
            )
                Check(map.GetProperty(name).ValueKind == JsonValueKind.Array && map.GetProperty(name).GetArrayLength() == 0, $"Supplementary JSON preserves {name} as an empty array");
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
            new PythonScript(app).WriteScriptToFile(script, target, header, json, includeTypeHeader: false);
            var text = File.ReadAllText(script);
            Check(
                !text.Contains("must-not-exist.h") && !text.Contains("%TYPE_HEADER_RELATIVE_PATH%") && !text.Contains("supplement_debug_info"),
                $"Generated Debug {target} script has no header path or alternate JSON processing mode"
            );
            Check(!File.Exists(Path.ChangeExtension(script, ".h")), $"Debug {target} generation does not emit a header");
            var automaticScript = Path.Combine(output, $"supplement-auto-{target}.py");
            new PythonScript(app).WriteScriptToFile(automaticScript, target, existingJsonMetadataFile: json, includeTypeHeader: false);
            Check(!File.Exists(Path.ChangeExtension(automaticScript, ".h")), $"Debug {target} does not generate a fallback header when none is supplied");
            var regularScript = Path.Combine(output, $"regular-{target}.py");
            new PythonScript(app).WriteScriptToFile(regularScript, target, header, json);
            Check(File.ReadAllText(regularScript).Contains("must-not-exist.h"), $"Non-Debug {target} retains its supplied header path by default");
        }
        Check(app.AnalysisMethods.Count == 0 && app.AnalysisTypes.Count == 0, "Supplementary output resolves references through the stock reflection model without building native graphs");
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
            "Native debug symbols cover the game's registration function and APIs emptied in the supplement"
        );
        Console.WriteLine($"Supplementary JSON: {new FileInfo(json).Length / 1048576.0:F1} MiB");
    }
}
