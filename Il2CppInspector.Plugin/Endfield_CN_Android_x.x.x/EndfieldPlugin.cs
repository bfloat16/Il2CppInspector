using Il2CppInspector.Plugins;

namespace Il2CppInspector.Plugin.Endfield;

public sealed class EndfieldPlugin : GamePlugin
{
    public override string GameId => EndfieldBuild.GameId;

    public override bool Matches(ReadOnlySpan<byte> metadata) => EndfieldMetadata.Matches(metadata);

    public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
    {
        status?.Invoke(this, $"Repairing {GameId} metadata header");
        var repaired = EndfieldMetadata.NormalizeTypeDefinitions(EndfieldMetadata.Fix(metadata), status);
        using var stream = new MemoryStream(repaired, writable: false);
        var decoded = Metadata.FromStream(stream, status);
        var image = FileFormatStream.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");
        var il2cppBinary =
            Il2CppBinary.Load(image, decoded, status)
            ?? throw new InvalidDataException($"Could not locate the {GameId} IL2CPP registration structures in the binary.");

        // Repaired records use the stock pipeline, so no game metadata adapter is attached.
        return new Il2CppInspector(il2cppBinary, decoded);
    }
}
