using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.Arknights;

public sealed class ArknightsPlugin : GamePlugin
{
    public override string GameId => ArknightsBuild.GameId;
    public override string RegistrationSignature => "";

    public override bool Matches(ReadOnlySpan<byte> metadata) => ArknightsMetadata.Matches(metadata);

    public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
    {
        status?.Invoke(this, "Repairing Arknights_CN_Windows_2.7.91 metadata");
        var repaired = ArknightsMetadata.NormalizeTypeDefinitions(ArknightsMetadata.Fix(metadata, status), status);
        using var metadataStream = new MemoryStream(repaired, writable: false);
        var decoded = Metadata.FromStream(metadataStream, status);

        var image = FileFormatStream.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");
        if (image.Arch != "x64")
            throw new InvalidDataException("Arknights_CN_Windows_2.7.91 requires an x64 GameAssembly.dll.");

        var il2cppBinary =
            Il2CppBinary.Load(image, decoded, status)
            ?? throw new InvalidDataException("Could not locate the Arknights_CN_Windows_2.7.91 IL2CPP registration structures in the binary.");

        return new Il2CppInspector(il2cppBinary, decoded);
    }

    // Repaired records use the stock pipeline, so no game metadata adapter is attached.
    public override UnityHeaders GetHeaders() => throw StockPipelineOnly();

    public override IGameTypeLayouts CreateTypeLayouts(TypeModel model) => throw StockPipelineOnly();

    public override GameNativeModel CreateNativeModel(TypeModel model) => throw StockPipelineOnly();

    public override IGameAnalysisModel CreateAnalysisModel(AppModel model) => throw StockPipelineOnly();

    private static InvalidOperationException StockPipelineOnly() => new("Arknights_CN_Windows_2.7.91 uses the stock analysis and output pipeline.");
}
