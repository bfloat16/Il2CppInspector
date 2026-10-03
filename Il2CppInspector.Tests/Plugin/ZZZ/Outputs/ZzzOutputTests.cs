namespace Il2CppInspector.Tests.Plugin.ZZZ.Outputs
{
    internal static class ZzzOutputTests
    {
        internal static void Run(ZzzTestContext context, string[] args)
        {
            var model = context.Model;
            var output = args[1];
            Directory.CreateDirectory(output);
            if (args.Contains("--write-csharp"))
            {
                new CSharpCodeStubs(model).WriteSingleFile(Path.Combine(output, "dump.cs"));
            }
            if (args.Contains("--write-dll"))
            {
                new AssemblyShims(model).Write(Path.Combine(output, "DummyDll"));
            }
            if (args.Contains("--suppressed-dll"))
            {
                ZzzAssemblyWriterTests.VerifySuppressed(context, output);
            }

            if (
                args.Contains("--write-native")
                || args.Contains("--write-header")
                || args.Contains("--refresh-script")
                || args.Contains("--write-project-pointers")
                || args.Contains("--write-json")
                || args.Contains("--write-project")
                || args.Contains("--finish-project")
                || args.Contains("--json-comments")
            )
            {
                var app = new AppModel(model, false).Build(new UnityVersion("2019.4.40f1"));
                IL2CPP.ZenlessZoneZero.ZzzRuntimeHeadersTests.Run(context, app);
                if (args.Contains("--write-native"))
                {
                    new PythonScript(app).WriteScriptToFile(Path.Combine(output, "il2cpp.py"), "IDA");
                }
                else
                {
                    if (args.Contains("--write-header"))
                    {
                        new CppScaffolding(app, useBetterArraySize: true).WriteTypes(Path.Combine(output, "il2cpp.h"));
                    }
                    new PythonScript(app).WriteScriptToFile(Path.Combine(output, "il2cpp.py"), "IDA", Path.Combine(output, "il2cpp.h"), Path.Combine(output, "il2cpp.json"));
                }
                if (args.Contains("--write-json"))
                {
                    new JSONMetadata(app).Write(Path.Combine(output, "il2cpp.json"));
                }
                if (args.Contains("--json-comments"))
                {
                    ZzzJsonMetadataTests.VerifyComments(app, output);
                }
                if (args.Contains("--write-project") || args.Contains("--finish-project"))
                {
                    ZzzCppScaffoldingTests.Run(app, output, args);
                }
                ZzzHeaderWriterTests.Run(context, app, output, args);
            }
            ZzzAssemblyWriterTests.VerifyAssemblies(context, output);
            if (!args.Contains("--dll-only"))
            {
                ZzzJsonMetadataTests.VerifyPayloads(context, output);
            }
        }
    }
}
