using System.Text;
using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Outputs;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.TOT
{
    /// <summary>
    /// Game plugin for 未定事件簿 (Tears of Themis) 6.1.0 Android.
    ///
    /// The metadata container is encrypted in two layers and its header is packer-permuted, so the
    /// stock metadata reader cannot be used. Load() decrypts the container, builds a Metadata object
    /// from the permuted header through the TotMetadata adapter (the same construction pattern ZZZ uses
    /// via Metadata.CreateForPlugin) and then lets the stock binary-registration discovery run, because
    /// TOT's libil2cpp.so registration structures are plaintext stock v24.1.
    ///
    /// Because the decoded records are stock v24.1, the adapter is suppressed after loading so the
    /// standard analysis/output pipeline is used; the adapter remains the sole decoder of the custom
    /// header. See Il2CppInspector.Common/Plugins/PluginMetadata.cs (SuppressGameAdapter).
    /// </summary>
    public sealed class TotPlugin : GamePlugin
    {
        public override string GameId => "TOT_CN_Android_6.1.0";
        public override UnityVersion DefaultUnityVersion => new("2018.4.36f1");
        public override string RegistrationSignature => "";

        public override bool Matches(ReadOnlySpan<byte> metadata) => TotMetadataDetector.IsTot(metadata);

        public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
        {
            var decrypted = TotMetadataDecryptor.Decrypt(metadata, status);

            var image = FileFormatStream.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");

            var adapter = new TotMetadata(decrypted, status, this);

            // TOT's registration structures are stock v24.1, so the standard discovery works. The code
            // heuristic is preferred because it needs no metadata; the data heuristic is the fallback.
            var il2cppBinary = Il2CppBinary.Load(image, adapter.Metadata, status) ?? throw new InvalidDataException("Could not locate the TOT IL2CPP registration structures in the binary.");

            adapter.ReadBinary(il2cppBinary);
            var package = new Il2CppInspector(il2cppBinary, adapter.Metadata);

            // Decoded records are stock v24.1: let the standard analysis and output pipeline run.
            adapter.Metadata.SuppressGameAdapter = true;
            return package;
        }

        public override UnityHeaders GetHeaders() => UnityHeaders.GetHeadersForVersion(DefaultUnityVersion);

        public override IGameTypeLayouts CreateTypeLayouts(TypeModel model) => new TotTypeLayouts();

        public override GameNativeModel CreateNativeModel(TypeModel model) => new TotNativeModel(model);

        public override IGameAnalysisModel CreateAnalysisModel(AppModel model) => new TotAnalysisModel(model);

        public override void WriteAssemblies(TypeModel model, string path, bool suppressMetadata, EventHandler<string> status) =>
            new AssemblyShims(model) { SuppressMetadata = suppressMetadata }.Write(path, status);

        public override void WriteHeader(AppModel model, string path, bool betterArraySize) => new CppScaffolding(model, betterArraySize).WriteTypes(path);

        public override void WriteJson(AppModel model, string path, bool allowComments, bool supplementDebugInfo) =>
            new JSONMetadata(model) { AllowComments = allowComments, SupplementDebugInfo = supplementDebugInfo }.Write(path);

        public override void WriteApplicationPointers(AppModel model, string path)
        {
            Directory.CreateDirectory(path);
            using (var functions = new StreamWriter(Path.Combine(path, "il2cpp-functions.h"), false, Encoding.UTF8))
            {
                functions.WriteLine("// TOT method and MethodInfo pointers; addresses are file-relative RVAs.");
                foreach (var method in model.Methods.Values.OrderBy(m => m.MethodCodeAddress))
                {
                    if (!method.HasCompiledCode)
                    {
                        continue;
                    }

                    functions.WriteLine(
                        $"DO_APP_FUNC(0x{method.MethodCodeAddress - model.Package.BinaryImage.ImageBase:X8}, {method.CppFnPtrType.ReturnType.Name}, {method.CppFnPtrType.Name}, ({string.Join(", ", method.CppFnPtrType.Arguments.Select(p => p.Type.Name))}));"
                    );
                }
            }

            using var types = new StreamWriter(Path.Combine(path, "il2cpp-types-ptr.h"), false, Encoding.UTF8);
            types.WriteLine("// TOT TypeInfo pointers; include inside the app namespace, as in the stock scaffolding.");
            var seen = new HashSet<string>();
            foreach (var usage in model.Package.MetadataUsages.Where(u => u.Type == MetadataUsageType.TypeInfo))
            {
                var type = model.TypeModel.TypesByReferenceIndex[usage.SourceIndex];
                var name = type.Name.ToCIdentifier();
                if (!seen.Add(name))
                {
                    continue;
                }

                types.WriteLine($"DO_TYPEDEF(0x{usage.VirtualAddress - model.Package.BinaryImage.ImageBase:X8}, {name});");
            }
        }
    }
}
