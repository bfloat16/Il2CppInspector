using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.TOT.OS
{
    /// <summary>
    /// Game plugin for 未定事件簿 (Tears of Themis) OS 5.8.1 Android.
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
    public sealed class TotOsPlugin : GamePlugin
    {
        public override string GameId => TotBuild.GameId;
        public override UnityVersion DefaultUnityVersion => new("2018.4.36f1");
        public override string RegistrationSignature => "";

        public override bool Matches(ReadOnlySpan<byte> metadata) => metadata.Length == TotBuild.MetadataSize && TotMetadataGate.IsTot(metadata);

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
    }
}
