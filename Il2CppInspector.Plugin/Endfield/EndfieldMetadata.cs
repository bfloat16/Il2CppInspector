using System.Buffers.Binary;

namespace Il2CppInspector.Plugin.Endfield
{
    internal static class EndfieldMetadata
    {
        private const uint Sanity = 0xFAB11BAF;
        private const int Version = 29;
        private const int OriginalHeaderSize = 0x100;
        private const int ModifiedHeaderSize = 0x108;
        private const int HeaderDelta = ModifiedHeaderSize - OriginalHeaderSize;
        private const int SectionCount = 31;
        private const int TypeDefinitions = 19;
        private const int Images = 20;
        private const int WindowsRuntimeTypeNames = 28;
        private const int WindowsRuntimeStrings = 29;
        private const int ExportedTypeDefinitions = 30;

        public static byte[] NormalizeTypeDefinitions(byte[] data, EventHandler<string> status = null)
        {
            var (typeOffset, typeSize) = ReadSection(data, TypeDefinitions);
            var (imageOffset, imageSize) = ReadSection(data, Images);
            if (imageSize % 40 != 0)
                throw new InvalidDataException("Endfield_CN_Android_x.x.x image definitions must use 40-byte records.");

            long typeCount = 0;
            for (var i = 0; i < imageSize / 40; i++)
            {
                var image = data.AsSpan(imageOffset + i * 40, 40);
                var start = BinaryPrimitives.ReadInt32LittleEndian(image[8..]);
                var count = BinaryPrimitives.ReadUInt32LittleEndian(image[12..]);
                if (start < 0)
                    throw new InvalidDataException("Endfield_CN_Android_x.x.x image type range is invalid.");
                typeCount = Math.Max(typeCount, start + (long)count);
            }

            if (typeSize == typeCount * 88)
                return data;
            if (typeSize != typeCount * 92)
                throw new InvalidDataException("Endfield_CN_Android_x.x.x type definitions do not match the image type ranges.");

            status?.Invoke(null, $"Normalizing Endfield_CN_Android_x.x.x type definitions ({typeCount} records, 92 -> 88 bytes)");
            var delta = checked((int)typeCount * 4);
            var typeEnd = checked(typeOffset + typeSize);
            var output = new byte[data.Length - delta];
            data.AsSpan(0, typeOffset).CopyTo(output);
            for (var i = 0; i < typeCount; i++)
            {
                var record = data.AsSpan(typeOffset + i * 92, 92);
                if (BinaryPrimitives.ReadUInt32LittleEndian(record[88..]) >> 24 != 2)
                    throw new InvalidDataException($"Endfield_CN_Android_x.x.x type definition {i} has an invalid token.");

                // Endfield inserts a 32-bit field before the stock member counts at byte 64.
                record[..64].CopyTo(output.AsSpan(typeOffset + i * 88));
                record[68..].CopyTo(output.AsSpan(typeOffset + i * 88 + 64));
            }
            data.AsSpan(typeEnd).CopyTo(output.AsSpan(typeEnd - delta));
            for (var i = 0; i < SectionCount; i++)
            {
                var (offset, size) = ReadSection(data, i);
                if (i == TypeDefinitions)
                {
                    WriteSection(output, i, offset, typeSize - delta);
                    continue;
                }
                if (offset != 0 && offset < typeEnd && (offset >= typeOffset || offset + (long)size > typeOffset))
                    throw new InvalidDataException($"Endfield_CN_Android_x.x.x metadata section {i} overlaps type definitions.");
                WriteSection(output, i, offset >= typeEnd ? offset - delta : offset, size);
            }
            return output;
        }

        public static bool Matches(ReadOnlySpan<byte> data) =>
            data.Length >= ModifiedHeaderSize
            && BinaryPrimitives.ReadUInt32LittleEndian(data) == Sanity
            && BinaryPrimitives.ReadInt32LittleEndian(data[4..]) == Version
            && BinaryPrimitives.ReadInt32LittleEndian(data[8..]) == ModifiedHeaderSize;

        public static byte[] Fix(ReadOnlySpan<byte> data)
        {
            if (!Matches(data))
            {
                throw new InvalidDataException("Endfield_CN_Android_x.x.x requires decoded v29 metadata with a 0x108-byte header.");
            }

            // The two Windows Runtime entries are replaced by the original repair algorithm.
            for (var i = 0; i < SectionCount; i++)
            {
                if (i is WindowsRuntimeTypeNames or WindowsRuntimeStrings)
                    continue;

                var (offset, size) = ReadSection(data, i);
                if (size < 0 || offset < 0 || offset > data.Length || size > data.Length - offset || (offset != 0 && offset < ModifiedHeaderSize) || (offset == 0 && size != 0))
                {
                    throw new InvalidDataException($"Endfield_CN_Android_x.x.x metadata section {i} is outside the payload.");
                }
            }

            var output = new byte[data.Length - HeaderDelta];
            data[ModifiedHeaderSize..].CopyTo(output.AsSpan(OriginalHeaderSize));
            BinaryPrimitives.WriteUInt32LittleEndian(output, Sanity);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), Version);

            for (var i = 0; i < SectionCount; i++)
            {
                if (i is WindowsRuntimeTypeNames or WindowsRuntimeStrings)
                    continue;

                var (offset, size) = ReadSection(data, i);
                WriteSection(output, i, offset == 0 ? 0 : offset - HeaderDelta, size);
            }

            var (exportedOffset, exportedSize) = ReadSection(output, ExportedTypeDefinitions);
            var runtimeNamesOffset = exportedOffset + exportedSize;
            WriteSection(output, WindowsRuntimeTypeNames, runtimeNamesOffset, output.Length - runtimeNamesOffset);
            WriteSection(output, WindowsRuntimeStrings, 0, 0);
            WriteSection(output, 0, OriginalHeaderSize, ReadSection(data, 0).Size);
            return output;
        }

        private static (int Offset, int Size) ReadSection(ReadOnlySpan<byte> data, int index)
        {
            var entry = data.Slice(8 + index * 8, 8);
            return (BinaryPrimitives.ReadInt32LittleEndian(entry), BinaryPrimitives.ReadInt32LittleEndian(entry[4..]));
        }

        private static void WriteSection(Span<byte> data, int index, int offset, int size)
        {
            var entry = data.Slice(8 + index * 8, 8);
            BinaryPrimitives.WriteInt32LittleEndian(entry, offset);
            BinaryPrimitives.WriteInt32LittleEndian(entry[4..], size);
        }
    }
}
