using Il2CppInspector.Model;
using Il2CppInspector.Plugin.Morax;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.BH3;

public sealed class Bh3Plugin : MoraxPlugin
{
    public override string GameId => "BH3_CN_Windows_9.1.0";

    private protected override Il2CppInspector Load(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status) =>
        Bh3Morax.Load(image, global, startup, status, this);

    private protected override (string Name, string Body)[] RuntimeRecords => Bh3RuntimeHeaders.Records;

    public override GameNativeModel CreateNativeModel(TypeModel model) => new Bh3NativeModel(model);
}
