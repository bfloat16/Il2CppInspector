using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Outputs;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.Endfield
{
    public sealed class EndfieldPlugin : GamePlugin
    {
        public override string GameId => "Endfield_CN";
        public override string RegistrationSignature => "";

        public override bool Matches(ReadOnlySpan<byte> metadata) => EndfieldMetadata.Matches(metadata);

        public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
        {
            status?.Invoke(this, "Repairing Endfield_CN metadata header");
            var repaired = EndfieldMetadata.NormalizeTypeDefinitions(EndfieldMetadata.Fix(metadata), status);
            using var stream = new MemoryStream(repaired, writable: false);
            var decoded = Metadata.FromStream(stream, status);
            var image = FileFormatStream.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");
            var il2cppBinary = Il2CppBinary.Load(image, decoded, status) ?? throw new InvalidDataException("Could not locate the Endfield_CN IL2CPP registration structures in the binary.");

            // Repaired records use the stock pipeline, so no game metadata adapter is attached.
            return new Il2CppInspector(il2cppBinary, decoded);
        }

        public override void WriteAssemblies(TypeModel model, string path, bool suppressMetadata, EventHandler<string> status) =>
            new AssemblyShims(model) { SuppressMetadata = suppressMetadata }.Write(path, status);

        public override void WriteHeader(AppModel model, string path, bool betterArraySize) => new CppScaffolding(model, betterArraySize).WriteTypes(path);

        public override void WriteJson(AppModel model, string path, bool allowComments, bool supplementDebugInfo) =>
            new JSONMetadata(model) { AllowComments = allowComments, SupplementDebugInfo = supplementDebugInfo }.Write(path);

        // Adapter hooks are not used: Unity headers, layouts and pointers come from the stock model.
        public override UnityHeaders GetHeaders() => throw StockPipelineOnly();

        public override IGameTypeLayouts CreateTypeLayouts(TypeModel model) => throw StockPipelineOnly();

        public override GameNativeModel CreateNativeModel(TypeModel model) => throw StockPipelineOnly();

        public override IGameAnalysisModel CreateAnalysisModel(AppModel model) => throw StockPipelineOnly();

        public override void WriteApplicationPointers(AppModel model, string path) => throw StockPipelineOnly();

        private static InvalidOperationException StockPipelineOnly() => new("Endfield_CN uses the stock analysis and output pipeline.");
    }
}
