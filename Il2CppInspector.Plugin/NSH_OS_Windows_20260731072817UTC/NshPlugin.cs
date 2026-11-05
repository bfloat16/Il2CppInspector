using Il2CppInspector.Plugins;

namespace Il2CppInspector.Plugin.NSH;

/// <summary>Loads the RC4-encrypted NSH common.dat metadata through the stock IL2CPP 27.1 pipeline.</summary>
public sealed class NshPlugin : GamePlugin
{
    public override string GameId => NshBuild.GameId;

    public override bool Matches(ReadOnlySpan<byte> metadata) =>
        metadata.Length == NshBuild.MetadataSize && metadata[..NshBuild.MetadataSignature.Length].SequenceEqual(NshBuild.MetadataSignature);

    public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
    {
        status?.Invoke(this, $"Decoding {GameId} common.dat metadata");
        using var stream = new MemoryStream(NshMetadata.Normalize(metadata), writable: false);
        var decoded = Metadata.FromStream(stream, status);
        var image = FileFormatStream.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");

        // 0x180C604B6 / 0x180C604C4 install stock registrations; Common discovers these without fixed addresses.
        var il2cppBinary =
            Il2CppBinary.Load(image, decoded, status)
            ?? throw new InvalidDataException($"Could not locate the {GameId} IL2CPP registration structures in the binary.");
        return new Il2CppInspector(il2cppBinary, decoded);
    }
}
