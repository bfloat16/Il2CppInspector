using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.Genshin;

public sealed class GenshinPlugin : GamePlugin
{
    public override string GameId => "Genshin_CN_Windows_7.1.0";
    public override string StartupMetadataFileName => "startup-metadata.dat";
    public override bool StreamExports => true;
    public override UnityVersion DefaultUnityVersion => new("2019.4.40f1");
    public override string RegistrationSignature => "void* (*Morax_MetadataCache_Register)()";
    public override string PythonMetadataProcessor => ResourceHelper.GetText(typeof(GenshinPlugin).Assembly, "Il2CppInspector.Plugin.Genshin.RuntimeCaches.py");

    public override bool Matches(ReadOnlySpan<byte> metadata) => metadata.Length >= 4 && metadata[..4].SequenceEqual("MHY\0"u8);

    public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
    {
        if (startupMetadata == null)
            throw new FileNotFoundException($"{GameId} requires startup-metadata.dat.");
        var image = PEReader.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");
        return GenshinMorax.Load(image, metadata, startupMetadata, status, this);
    }

    public override UnityHeaders GetHeaders() => UnityHeaders.ForPlugin(new("2019.4.24"), DefaultUnityVersion, GenshinRuntimeHeaders.Apply);

    public override IGameTypeLayouts CreateTypeLayouts(TypeModel model)
    {
        var adapter = (GenshinMorax)model.Package.Metadata.GameAdapter;
        return new SharedTypeLayouts(model, adapter.GenericLayoutGroups, adapter.EnableEnumSharing);
    }

    public override GameNativeModel CreateNativeModel(TypeModel model) => new GenshinNativeModel(model);

    public override IGameAnalysisModel CreateAnalysisModel(AppModel model)
    {
        ((GenshinNativeModel)model.GameNativeModel).InitializeNaming(model);
        model.BuildMetadataDataUsages();
        return new NativeLayoutAnalysisModel(model);
    }
}
