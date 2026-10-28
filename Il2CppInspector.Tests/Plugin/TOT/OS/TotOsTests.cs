extern alias tot_os;

using System.Security.Cryptography;
using Il2CppInspector.Plugin.TOT;
using OsDecryptor = tot_os::Il2CppInspector.TotMetadataDecryptor;
using OsMetadata = tot_os::Il2CppInspector.TotMetadata;
using OsPlugin = tot_os::Il2CppInspector.Plugin.TOT.OS.TotOsPlugin;

namespace Il2CppInspector.Tests.Plugin.TOT.OS
{
    internal static class TotOsTests
    {
        private const string GameId = "TOT_OS_Android_5.8.1";
        private const int MetadataSize = 39919768;

        internal static void RunSelection()
        {
            var os = GamePlugins.Get(GameId.ToLowerInvariant());
            var cn = GamePlugins.Get("TOT_CN_Android_6.1.0");
            // Discovery may use a separate plugin load context from the direct fixture reference.
            Check(
                os.GetType().FullName == "Il2CppInspector.Plugin.TOT.OS.TotOsPlugin"
                    && os.GetType().Assembly.GetName().Name == "Il2CppInspector.Plugin.TOT.OS",
                "TOT OS is discovered as its own managed plugin"
            );
            var metadata = new byte[MetadataSize];
            Check(!os.Matches(metadata), "TOT OS requires the keybox gate as well as the build size");
            new byte[] { 0x2E, 0xFC, 0xFE, 0x2C }.CopyTo(metadata, metadata.Length - 0x4000 + 0xC8);
            Check(os.Matches(metadata) && !cn.Matches(metadata), "OS metadata matches only the OS build");
            Check(ReferenceEquals(GamePlugins.Select(null, metadata), os), "TOT OS metadata is selected without --game");
            metadata[metadata.Length - 0x4000 + 0xC8] ^= 1;
            Check(!os.Matches(metadata), "TOT OS rejects a damaged keybox gate");

            var cnMetadata = new byte[39779644];
            new byte[] { 0x2E, 0xFC, 0xFE, 0x2C }.CopyTo(cnMetadata, cnMetadata.Length - 0x4000 + 0xC8);
            Check(cn.Matches(cnMetadata) && !os.Matches(cnMetadata), "CN metadata matches only the CN build");
            Check(ReferenceEquals(GamePlugins.Select(null, cnMetadata), cn), "TOT CN automatic selection remains unambiguous");
            Check(!os.Matches(ReadOnlySpan<byte>.Empty), "TOT OS rejects truncated metadata");
        }

        internal static void Run(string nativeDirectory)
        {
            RunSelection();
            var raw = File.ReadAllBytes(Path.Combine(nativeDirectory, "global-metadata.dat"));
            var originalHash = Sha256(raw);
            var plugin = new OsPlugin();
            Check(plugin.Matches(raw) && !new TotPlugin().Matches(raw), "Real OS input is distinguished from CN");
            var outer = OsDecryptor.DecryptOuter(raw);
            Check(
                Sha256(outer) == "cbcfc0a4c07a74c43747a1250533051fb37362efdfad545a68c14433cd3959b1",
                "OS outer plaintext matches the independently recovered CBC IV and round keys"
            );
            var plain = OsDecryptor.Decrypt(raw, null);
            Check(
                Sha256(plain) == "e63cf3f1918b530bf6c3d6e2ba05954ec358e8e730b708047abfcfb5b0db0349",
                "OS full plaintext matches the independent Python implementation"
            );
            Check(Sha256(raw) == originalHash, "OS decryption preserves the input buffer");
            var metadata = new OsMetadata(plain, null, plugin).Metadata;
            Check(metadata.Types.Length == 29548 && metadata.Methods.Length == 228587, "OS decodes 29548 types and 228587 methods");
            Check(metadata.Fields.Length == 228551 && metadata.Params.Length == 213683, "OS decodes its field and parameter tables");
            Check(metadata.Images.Length == 77 && metadata.Assemblies.Length == 77, "OS decodes 77 images and assemblies");
            Check(
                metadata.StringLiterals.Length == 36922 && metadata.StringLiterals[0].StartsWith("Searched all reachable nodes,"),
                "OS literal count and plaintext agree with native analysis"
            );
            Check(
                metadata.Strings[metadata.Types[0].NameIndex] == "<Module>" && metadata.Strings[metadata.Images[0].NameIndex] == "mscorlib.dll",
                "OS name indices resolve through the OS name table"
            );
            Check(metadata.Strings[metadata.Assemblies[0].Aname.NameIndex] == "mscorlib", "OS preserves the assembly identity at string index zero");

            var package =
                Inspector.LoadFromFile(Path.Combine(nativeDirectory, "libil2cpp.so"), Path.Combine(nativeDirectory, "global-metadata.dat"))?.Single()
                ?? throw new InvalidOperationException("Could not load TOT OS inputs.");
            Check(package.Metadata.GamePlugin.GameId == GameId, "Full native loading selects the OS plugin automatically");
            Check(
                package.Metadata.SuppressGameAdapter && !package.Metadata.HasGameAdapter,
                "OS uses the standard analysis pipeline after normalization"
            );
            Check(
                package.TypeReferences.Length == 219790 && package.MetadataUsages.Count == 170539,
                "OS standard registration parsing recovers all type references and metadata usages"
            );
            var model = new TypeModel(package);
            var app = new AppModel(model, false).Build();
            Check(app.Types.Count == 87417 && app.Methods.Count == 369742, "OS standard analysis recovers the validated native types and methods");
            var output = Directory.CreateTempSubdirectory("tot-os-tests-").FullName;
            try
            {
                new JSONMetadata(app).Write(Path.Combine(output, "metadata.json"));
                new CppScaffolding(app, true).WriteTypes(Path.Combine(output, "il2cpp-types.h"));
                Check(
                    new FileInfo(Path.Combine(output, "metadata.json")).Length > 0 && new FileInfo(Path.Combine(output, "il2cpp-types.h")).Length > 0,
                    "OS exports JSON and C++ through standard writers"
                );
                var pointers = Path.Combine(output, "pointers");
                plugin.WriteApplicationPointers(app, pointers);
                Check(
                    new FileInfo(Path.Combine(pointers, "il2cpp-functions.h")).Length > 0
                        && new FileInfo(Path.Combine(pointers, "il2cpp-types-ptr.h")).Length > 0,
                    "OS exports native pointers through the inherited standard writer"
                );
                var assemblies = Path.Combine(output, "DummyDll");
                Directory.CreateDirectory(assemblies);
                plugin.WriteAssemblies(model, assemblies, true, null);
                Check(Directory.GetFiles(assemblies, "*.dll").Length == 77, "OS exports all 77 managed assemblies through the standard writer");
            }
            finally
            {
                Directory.Delete(output, true);
            }
        }

        private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }
}
