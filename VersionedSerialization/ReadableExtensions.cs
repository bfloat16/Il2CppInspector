namespace VersionedSerialization;

public static class ReadableExtensions
{
    extension<TReadable>(TReadable)
        where TReadable : IReadable, new()
    {
        public static int StructSize(in StructVersion version = default, in ReaderConfig config = default) => TReadable.Size(version, config);

        public static TReadable FromBytes(
            ReadOnlySpan<byte> data,
            bool littleEndian = true,
            ReaderConfig config = default,
            in StructVersion version = default
        ) => Reader.ReadVersionedObject<TReadable>(data, version, littleEndian: littleEndian, config: config);
    }
}
