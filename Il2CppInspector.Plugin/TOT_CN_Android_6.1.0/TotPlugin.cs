using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.TOT
{
    /// <summary>
    /// 未定事件簿 (Tears of Themis) Android. The metadata is two-layer encrypted with a permuted header;
    /// TotMetadata decodes it to stock v24.1 records, and the binary registrations are plaintext stock v24.1.
    /// </summary>
    public sealed class TotPlugin : GamePlugin
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

            var il2cppBinary =
                Il2CppBinary.Load(image, adapter.Metadata, status)
                ?? throw new InvalidDataException("Could not locate the TOT IL2CPP registration structures in the binary.");

            adapter.ReadBinary(il2cppBinary);
            var package = new Il2CppInspector(il2cppBinary, adapter.Metadata);

            // The decoded records are stock, so the standard analysis and outputs apply.
            adapter.Metadata.SuppressGameAdapter = true;
            return package;
        }

        public override UnityHeaders GetHeaders() => UnityHeaders.GetHeadersForVersion(DefaultUnityVersion);

        public override IGameTypeLayouts CreateTypeLayouts(TypeModel model) => new TotTypeLayouts();

        public override GameNativeModel CreateNativeModel(TypeModel model) => new TotNativeModel(model);

        public override IGameAnalysisModel CreateAnalysisModel(AppModel model) => new TotAnalysisModel(model);
    }
}
