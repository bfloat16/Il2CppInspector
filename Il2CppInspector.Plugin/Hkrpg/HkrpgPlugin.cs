using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.Hkrpg;

public sealed class HkrpgPlugin : GamePlugin
{
    public override string GameId => "HSR_CN_Windows_4.6.51";
    public override string StartupMetadataFileName => "startup-metadata.dat";
    public override bool StreamExports => true;
    public override string RegistrationSignature => "void* (*Morax_MetadataCache_Register)()";
    public override string PythonMetadataProcessor => ResourceHelper.GetText(typeof(HkrpgPlugin).Assembly, "Il2CppInspector.Plugin.Hkrpg.RuntimeCaches.py");

    public override bool Matches(ReadOnlySpan<byte> metadata) => metadata.Length >= 4 && metadata[..4].SequenceEqual("MHY\0"u8);

    public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
    {
        if (startupMetadata == null)
            throw new FileNotFoundException($"{GameId} requires startup-metadata.dat. Supply --startup-metadata or place it beside global-metadata.dat.");
        var image = PEReader.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");
        return HkrpgMorax.Load(image, metadata, startupMetadata, status, this);
    }

    // Physical records are verified against this build; unrecovered records remain opaque.
    public override UnityHeaders GetHeaders() => UnityHeaders.ForPlugin(new("2019.4.24"), null, HkrpgRuntimeHeaders.Apply);

    public override IGameTypeLayouts CreateTypeLayouts(TypeModel model) => new SharedTypeLayouts(model, new Dictionary<int, int>(), false);

    public override GameNativeModel CreateNativeModel(TypeModel model) => new HkrpgNativeModel(model);

    public override IGameAnalysisModel CreateAnalysisModel(AppModel model)
    {
        ((HkrpgNativeModel)model.GameNativeModel).InitializeNaming(model);
        model.BuildMetadataDataUsages();
        return new NativeLayoutAnalysisModel(model);
    }
}
