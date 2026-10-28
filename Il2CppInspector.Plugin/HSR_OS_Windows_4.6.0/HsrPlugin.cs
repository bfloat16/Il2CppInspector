using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Plugin.Morax;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.HSR;

public sealed class HsrPlugin : MoraxPlugin
{
    public override string GameId => HsrBuild.GameId;

    // No runtime Unity version is verified for this build; unrecovered records stay opaque.
    public override UnityVersion DefaultUnityVersion => null;

    private protected override Il2CppInspector Load(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status) =>
        HsrMorax.Load(image, global, startup, status, this);

    private protected override (string Name, string Body)[] RuntimeRecords => HsrRuntimeHeaders.Records;

    public override GameNativeModel CreateNativeModel(TypeModel model) => new HsrNativeModel(model);
}
