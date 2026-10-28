using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.LAD
{
    /// <summary>
    /// Loads the CN Android 6.0.0 X3Engine fork. Metadata records are normalized to v29 for the stock
    /// attribute/default-value readers; native layout differences are supplied through Common's model.
    /// </summary>
    public sealed class LadPlugin : GamePlugin
    {
        public override string GameId => LadBuild.GameId;
        public override UnityVersion DefaultUnityVersion => new("2019.4.34f1");

        // 0x1C4F910 / 0x24F1344 pass registrations and CodeGenOptions in X0/X1/X2.
        public override string RegistrationSignature =>
            "void (*x3_register)(const Il2CppCodeRegistration* codeRegistration, const Il2CppMetadataRegistration* metadataRegistration, const Il2CppCodeGenOptions* codeGenOptions)";

        public override bool Matches(ReadOnlySpan<byte> metadata) =>
            metadata.Length == LadBuild.MetadataSize
            && LadRecords.U32(metadata, 0) == LadBuild.SanityEncoded
            && LadRecords.U32(metadata, 4) == LadBuild.VersionEncoded;

        public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
        {
            var image = FileFormatStream.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");
            var adapter = new LadMetadata(metadata, status, this);
            return LadBinary.Load(image, adapter, status);
        }

        // Start from the v24.5 definitions and apply the binary-verified runtime differences.
        public override UnityHeaders GetHeaders() => LadRuntimeHeaders.GetHeaders();

        public override IGameTypeLayouts CreateTypeLayouts(TypeModel model) => new LadTypeLayouts();

        public override GameNativeModel CreateNativeModel(TypeModel model) => new LadNativeModel(model);

        public override IGameAnalysisModel CreateAnalysisModel(AppModel model)
        {
            ((LadNativeModel)model.GameNativeModel).InitializeNaming(model);
            model.BuildMetadataDataUsages();
            return new NativeLayoutAnalysisModel(model);
        }
    }
}
