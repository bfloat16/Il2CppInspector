using Il2CppInspector.Model;
using Il2CppInspector.Plugin.Morax;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.Genshin;

public sealed class GenshinPlugin : MoraxPlugin
{
    public override string GameId => "Genshin_CN_Windows_7.1.0";

    private protected override Il2CppInspector Load(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status) =>
        GenshinMorax.Load(image, global, startup, status, this);

    private protected override (string Name, string Body)[] RuntimeRecords => GenshinRuntimeHeaders.Records;

    public override GameNativeModel CreateNativeModel(TypeModel model) => new GenshinNativeModel(model);
}
