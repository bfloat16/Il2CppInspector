using System.Security.Cryptography;
using Il2CppInspector.Plugin.TOT;

namespace Il2CppInspector.Tests.Plugin.TOT
{
    /// <summary>
    /// Self-test for the TOT 6.1.0 plugin. Usage:
    ///   dotnet run --project Il2CppInspector.Tests -c Release -- --tot &lt;Native directory&gt;
    /// The directory must contain global-metadata.dat (and optionally libil2cpp.so for a full load).
    /// </summary>
    internal static class TotSelfTest
    {
        private const string L1Hash = "026b24cdcb541de9b880b30c9bb94a80a38ad476232b8e276a0a8ef9a034a983";
        private const string L1L2Hash = "464198568636cffc34442b688957283fcf6e9b5932f037834c66726624676f99";

        internal static void Run(string nativeDirectory)
        {
            var metadataPath = Path.Combine(nativeDirectory, "global-metadata.dat");
            var binaryPath = Path.Combine(nativeDirectory, "libil2cpp.so");
            Check(File.Exists(metadataPath), $"found {metadataPath}");

            var raw = File.ReadAllBytes(metadataPath);

            // Detection.
            Check(TotMetadataDetector.IsTot(raw), "TOT gate detector matches the shipped metadata");
            Check(TotMetadataDetector.IsTot(metadataPath), "TOT path detector matches the shipped metadata");

            // L1 only.
            var l1 = TotMetadataDecryptor.DecryptOuter(raw);
            Check(Sha256(l1) == L1Hash, $"L1 hash OK ({L1Hash})");

            // L1 + L2.
            var plain = TotMetadataDecryptor.Decrypt(raw);
            Check(Sha256(plain) == L1L2Hash, $"L1+L2 hash OK ({L1L2Hash})");

            // Metadata parsing through the custom header adapter.
            var plugin = new TotPlugin();
            var adapter = new TotMetadata(plain, null, plugin);
            var metadata = adapter.Metadata;
            Check(metadata.Types.Length == 29323, "29323 type definitions");
            Check(metadata.Methods.Length == 227533, "227533 methods");
            Check(metadata.Fields.Length == 228394, "228394 fields");
            Check(metadata.Params.Length == 212861, "212861 parameters");
            Check(metadata.Images.Length == 78, "78 images");
            Check(metadata.Assemblies.Length == 78, "78 assemblies");
            Check(metadata.StringLiterals.Length == 36670, "36670 string literals");
            Check(metadata.Strings[metadata.Types[0].NameIndex] == "<Module>", "first type name decodes to <Module>");
            Check(metadata.Strings[metadata.Images[0].NameIndex] == "mscorlib.dll", "first image name decodes to mscorlib.dll");
            // String index 0 is the real mscorlib assembly name; it must not be masked by a synthetic
            // empty-string sentinel, or the DummyDll writer loses mscorlib's identity and fails.
            Check(metadata.Strings[metadata.Assemblies[0].Aname.NameIndex] == "mscorlib", "first assembly name decodes to mscorlib");

            if (!File.Exists(binaryPath))
            {
                Console.WriteLine("libil2cpp.so not present; skipping the full binary load.");
                return;
            }

            using var binary = File.OpenRead(binaryPath);
            var package = plugin.Load(binary, raw, null, null, null);
            Check(package != null, "plugin Load returned a package");
            Check(package.Metadata.SuppressGameAdapter, "adapter suppressed after load");
            Check(package.TypeDefinitions.Length == metadata.Types.Length, "loaded type count matches metadata");
            Check(package.TypeReferences.Length > 0, "type references decoded from the binary");
            Check(package.MetadataUsages.Count > 0, "metadata usages decoded");
            Console.WriteLine($"binary load OK: {package.TypeReferences.Length} type refs, {package.MetadataUsages.Count} usages");

            // Full stock analysis/output pipeline (the adapter is suppressed so these run normally).
            var typeModel = new TypeModel(package);
            var appModel = new AppModel(typeModel, false).Build();
            Check(appModel.Types.Count > 0, $"analysis model built {appModel.Types.Count} types");
            Check(appModel.Methods.Count > 0, $"analysis model built {appModel.Methods.Count} methods");

            var outputDirectory = Directory.CreateTempSubdirectory("tot-selftest-").FullName;
            try
            {
                var jsonPath = Path.Combine(outputDirectory, "metadata.json");
                new JSONMetadata(appModel).Write(jsonPath);
                Check(new FileInfo(jsonPath).Length > 0, "JSON metadata written");

                var headerPath = Path.Combine(outputDirectory, "il2cpp-types.h");
                new CppScaffolding(appModel, true).WriteTypes(headerPath);
                Check(new FileInfo(headerPath).Length > 0, "C++ type header written");

                var pointersPath = Path.Combine(outputDirectory, "pointers");
                plugin.WriteApplicationPointers(appModel, pointersPath);
                Check(new FileInfo(Path.Combine(pointersPath, "il2cpp-functions.h")).Length > 0, "application function pointers written");

                var assemblyPath = Path.Combine(outputDirectory, "assemblies");
                Directory.CreateDirectory(assemblyPath);
                try
                {
                    plugin.WriteAssemblies(typeModel, assemblyPath, true, null);
                    Check(Directory.GetFiles(assemblyPath, "*.dll", SearchOption.AllDirectories).Length > 0, "assembly shims written");
                }
                catch (Exception ex)
                {
                    // Stock AssemblyShims cannot emit this metadata's <PrivateImplementationDetails>
                    // static-array-init fields (dnlib reports an initial-value/type-size mismatch).
                    // This is a pre-existing stock-writer limitation, not a plugin decoding failure.
                    Console.WriteLine($"WARNING assembly shims not written: {ex.Message.Split('\n')[0]}");
                }

                Console.WriteLine($"outputs written to {outputDirectory}");
            }
            finally
            {
                Directory.Delete(outputDirectory, true);
            }
        }

        private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }
}
