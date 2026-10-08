using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.ZZZ
{
    public sealed class ZzzPlugin : GamePlugin
    {
        public override string GameId => "ZZZ_CN_3.2.0";
        public override string StartupMetadataFileName => "startup-metadata.dat";
        public override bool StreamExports => true;
        public override UnityVersion DefaultUnityVersion => new("2019.4.40f1");
        public override string RegistrationSignature => "void* (*Morax_MetadataCache_Register)()";
        public override string PythonMetadataProcessor => ResourceHelper.GetText(typeof(ZzzPlugin).Assembly, "Il2CppInspector.Plugin.ZZZ.RuntimeCaches.py");

        public override bool Matches(ReadOnlySpan<byte> metadata) => ZzzMetadataDetector.IsMorax(metadata);

        public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
        {
            if (startupMetadata == null)
            {
                throw new FileNotFoundException($"{GameId} requires startup-metadata.dat. Supply --startup-metadata or LoadOptions.StartupMetadataPath.");
            }

            var image = PEReader.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");
            return ZzzMorax.Load(image, metadata, startupMetadata, status, this);
        }

        public override UnityHeaders GetHeaders() => UnityHeaders.ForPlugin(new("2019.4.24"), DefaultUnityVersion, ZzzRuntimeHeaders.Apply);

        public override IGameTypeLayouts CreateTypeLayouts(TypeModel model) => new ZzzSharedTypeLayouts(model);

        public override GameNativeModel CreateNativeModel(TypeModel model) => new ZzzNativeModel(model);

        public override IGameAnalysisModel CreateAnalysisModel(AppModel model)
        {
            ((ZzzNativeModel)model.GameNativeModel).InitializeNaming(model);
            model.BuildMetadataDataUsages();
            return new NativeLayoutAnalysisModel(model);
        }
    }
}
