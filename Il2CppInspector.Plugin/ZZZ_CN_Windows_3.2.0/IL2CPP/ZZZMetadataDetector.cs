namespace Il2CppInspector
{
    /// <summary>Detects the ZZZ/MORAX metadata container before the stock Unity reader runs.</summary>
    public static class ZzzMetadataDetector
    {
        internal static bool IsMorax(ReadOnlySpan<byte> bytes) => bytes.Length >= 4 && bytes[..4].SequenceEqual("MHY\0"u8);

        public static bool IsZzz(string metadataPath)
        {
            if (!File.Exists(metadataPath))
            {
                return false;
            }

            Span<byte> header = stackalloc byte[4];
            using var stream = File.OpenRead(metadataPath);
            return stream.Read(header) == header.Length && IsMorax(header);
        }

        public static string FindStartupMetadata(string metadataPath)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(metadataPath)) ?? ".";
            var candidate = Path.Combine(directory, "startup-metadata.dat");
            return File.Exists(candidate) ? candidate : null;
        }
    }
}
