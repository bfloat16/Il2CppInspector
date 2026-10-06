using Il2CppInspector.Plugin.TOT;

namespace Il2CppInspector.Tests.Plugin.TOT;

internal static class TotNativeOutputTests
{
    internal static void Run(string nativeDirectory, string outputDirectory)
    {
        var package =
            Inspector.LoadFromFile(Path.Combine(nativeDirectory, "libil2cpp.so"), Path.Combine(nativeDirectory, "global-metadata.dat"))?.Single()
            ?? throw new InvalidOperationException("Could not load TOT inputs.");
        Check(package.Metadata.GamePlugin is TotPlugin && package.Metadata.GamePlugin.RegistrationSignature == "", "TOT retains plugin identity and its unused empty registration declaration");
        Check(package.Metadata.SuppressGameAdapter && !package.Metadata.HasGameAdapter, "TOT native outputs use the standard pipeline after decoding");
        Check(DebugOutput.SelectFormat(package.BinaryImage) == DebugSymbolFormat.Dwarf, "Debug automatically routes the actual TOT ELF image to DWARF");
        var app = new AppModel(new TypeModel(package), false).Build(package.Metadata.GamePlugin.DefaultUnityVersion);
        var objectEquals = app
            .TypeModel.TypesByFullName["System.Object"]
            .DeclaredMethods.Single(m => m.Name == "Equals" && !m.IsStatic && m.DeclaredParameters.Count == 1 && m.DeclaredParameters[0].ParameterType.FullName == "System.Object");
        Check(MangledNameBuilder.Method(objectEquals) == "_ZN6System6Object6EqualsES0_", "Itanium substitution S0_ refers to System.Object, not its System namespace");
        if (objectEquals.VirtualAddress is { Start: not 0 } equalsAddress)
            Check(
                app.EnumerateNativeMethods().Any(m => m.Address == equalsAddress.Start && m.LinkageName == MangledNameBuilder.Method(objectEquals)),
                "Native DWARF names use the same method identity as IDA JSON names"
            );
        var signature = app.GetRegistrationSignature();
        Check(
            signature.Name == "il2cpp_codegen_register" && signature.ReturnType.Name == "void" && signature.Arguments.Count == 3,
            "TOT selects the standard three-parameter Unity registration declaration"
        );
        Check(
            signature.Arguments.Select(a => a.Name).SequenceEqual(["codeRegistration", "metadataRegistration", "codeGenOptions"]),
            "TOT registration declaration preserves the standard parameter names"
        );
        var parameterTypes = new[] { "Il2CppCodeRegistration", "Il2CppMetadataRegistration", "Il2CppCodeGenOptions" };
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            var pointer = signature.Arguments[i].Type as CppPointerType;
            var constant = pointer?.ElementType as CppConstType;
            var element = constant?.ElementType;
            while (element is CppAlias alias)
                element = alias.ElementType;
            Check(
                pointer?.Size == package.BinaryImage.Bits && constant != null && ReferenceEquals(element, app.RuntimeCppTypes.GetType(parameterTypes[i], returnUnaliased: true)),
                $"Registration parameter {parameterTypes[i]} points to the existing const runtime type"
            );
        }
        var support = app.EnumerateNativeSupportMethods().ToArray();
        if (package.Binary.RegistrationFunctionPointer != 0)
        {
            Check(
                support.Any(m => m.Name == signature.Name && m.Address == package.Binary.RegistrationFunctionPointer && m.Signature?.Arguments.Count == 3),
                "Native debug enumeration includes the standard TOT registration function"
            );
            var mapped = app.GetAddressMap()[package.Binary.RegistrationFunctionPointer] as CppFnPtrType;
            Check(mapped?.Name == signature.Name && mapped.Arguments.Count == signature.Arguments.Count, "Address mapping and native debug output agree on TOT registration types");
        }
        else
        {
            Check(!support.Any(m => m.Name == signature.Name), "A missing registration address emits no synthetic native function");
        }
        Directory.CreateDirectory(outputDirectory);
        var output = Path.Combine(outputDirectory, "libil2cpp.sym");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = new DwarfOutput(app).Write(output, (_, message) => Console.WriteLine(message));
        Check(result.Functions > 0 && result.TypedFunctions > 0 && result.TypeRecords > 0, "TOT exports typed DWARF directly from its standard analysis pipeline");
        Console.WriteLine($"TOT DWARF: {timer.Elapsed.TotalSeconds:F1}s; {new FileInfo(output).Length / 1048576.0:F1} MiB");
    }
}
