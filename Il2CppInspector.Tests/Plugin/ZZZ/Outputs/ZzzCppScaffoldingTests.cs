namespace Il2CppInspector.Tests.Plugin.ZZZ.Outputs
{
    internal static class ZzzCppScaffoldingTests
    {
        internal static void Run(AppModel app, string output, string[] args)
        {
            var scaffolding = new CppScaffolding(app, useBetterArraySize: true);
            if (args.Contains("--finish-project"))
            {
                var existingHeader = File.Exists(Path.Combine(output, "il2cpp.h")) ? Path.Combine(output, "il2cpp.h") : Path.Combine(output, "CppScaffolding", "appdata", "il2cpp-types.h");
                typeof(CppScaffolding)
                    .GetMethod("WriteProject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    .Invoke(scaffolding, [Path.Combine(output, "CppScaffolding"), existingHeader]);
            }
            else
            {
                scaffolding.Write(Path.Combine(output, "CppScaffolding"));
            }
            File.Copy(Path.Combine(output, "CppScaffolding", "appdata", "il2cpp-types.h"), Path.Combine(output, "il2cpp.h"), true);
            Check(
                File.ReadAllText(Path.Combine(output, "CppScaffolding", "appdata", "il2cpp-api-functions-ptr.h")).Contains("#define il2cpp_get_api_table_ptr 0x002B7040"),
                "C++ API pointer macros use RVA rather than raw PE file offsets"
            );
            Check(
                File.ReadAllText(Path.Combine(output, "CppScaffolding", "appdata", "il2cpp-metadata-version.h")).Contains("#define __IL2CPP_METADATA_VERSION 245"),
                "Project metadata version encodes 24.5 as 245"
            );
        }
    }
}
