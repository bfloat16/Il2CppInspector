namespace Il2CppInspector.Plugin.NSH;

internal static class NshBuild
{
    internal const string GameId = "NSH_OS_Windows_20260731072817UTC";
    internal const int MetadataSize = 24683324;

    // The RC4-encrypted header contains build-specific table offsets and padding (0x180C597C0).
    internal static ReadOnlySpan<byte> MetadataSignature =>
        [0xA9, 0x13, 0xE8, 0x05, 0x2A, 0x52, 0x83, 0x6A, 0x85, 0xD1, 0x53, 0x35, 0x62, 0x7A, 0x94, 0x57];
}
