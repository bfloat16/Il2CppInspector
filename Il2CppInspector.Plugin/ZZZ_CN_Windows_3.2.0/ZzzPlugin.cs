using Il2CppInspector.Model;
using Il2CppInspector.Plugin.Morax;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.ZZZ;

public sealed class ZzzPlugin : MoraxPlugin
{
    public override string GameId => ZzzBuild.GameId;

    private protected override Il2CppInspector Load(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status) =>
        ZzzMorax.Load(image, global, startup, status, this);

    private protected override (string Name, string Body)[] RuntimeRecords => ZzzRuntimeHeaders.Records;

    public override GameNativeModel CreateNativeModel(TypeModel model) => new ZzzNativeModel(model);
}
