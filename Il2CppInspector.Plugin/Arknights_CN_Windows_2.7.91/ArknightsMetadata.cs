using System.Buffers.Binary;

namespace Il2CppInspector.Plugin.Arknights;

internal static class ArknightsMetadata
{
    private const uint Sanity = 0xFAB11BAF;
    private const int Version = 29;
    private const int OriginalHeaderSize = 0x100;
    private const int ModifiedHeaderSize = 0x110;
    private const int HeaderDelta = ModifiedHeaderSize - OriginalHeaderSize;
    private const int SectionCount = 31;
    private const int TypeDefinitions = 19;
    private const int Images = 20;
    private const int ImageDefinitionSize = 40;
    private const int TypeCount = 51811;

    public static bool Matches(ReadOnlySpan<byte> data) =>
        data.Length == ArknightsBuild.MetadataSize
        && BinaryPrimitives.ReadUInt32LittleEndian(data) == Sanity
        && BinaryPrimitives.ReadInt32LittleEndian(data[4..]) == Version
        && BinaryPrimitives.ReadInt32LittleEndian(data[8..]) == ModifiedHeaderSize
        && ReadSection(data, TypeDefinitions).Size == TypeCount * 92
        && ReadSection(data, Images).Size % ImageDefinitionSize == 0;

    public static byte[] Fix(ReadOnlySpan<byte> data, EventHandler<string> status)
    {
        ValidateSections(data);

        status?.Invoke(null, "Removing Arknights_CN_Windows_2.7.91 metadata header entries");

        // Arknights appends two custom section entries after the stock v29 table.
        var output = new byte[data.Length - HeaderDelta];
        data[ModifiedHeaderSize..].CopyTo(output.AsSpan(OriginalHeaderSize));
        BinaryPrimitives.WriteUInt32LittleEndian(output, Sanity);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), Version);

        for (var index = 0; index < SectionCount; index++)
        {
            var (offset, size) = ReadSection(data, index);
            WriteSection(output, index, offset == 0 ? 0 : offset - HeaderDelta, size);
        }
        return output;
    }

    public static byte[] NormalizeTypeDefinitions(byte[] data, EventHandler<string> status)
    {
        var (typeOffset, typeSize) = ReadSection(data, TypeDefinitions);
        if (typeSize != TypeCount * 92)
            throw new InvalidDataException("Arknights_CN_Windows_2.7.91 type definitions do not match the image type ranges.");

        status?.Invoke(null, $"Normalizing Arknights_CN_Windows_2.7.91 type definitions ({TypeCount} records, 92 -> 88 bytes)");
        var delta = checked(TypeCount * 4);
        var typeEnd = typeOffset + typeSize;
        var output = new byte[data.Length - delta];
        data.AsSpan(0, typeOffset).CopyTo(output);

        for (var index = 0; index < TypeCount; index++)
        {
            var record = data.AsSpan(typeOffset + index * 92, 92);
            if (BinaryPrimitives.ReadUInt32LittleEndian(record[88..]) >> 24 != 2)
                throw new InvalidDataException($"Arknights_CN_Windows_2.7.91 type definition {index} has an invalid token.");

            // Arknights inserts a 32-bit field before the stock member counts at byte 64.
            record[..64].CopyTo(output.AsSpan(typeOffset + index * 88));
            record[68..].CopyTo(output.AsSpan(typeOffset + index * 88 + 64));
        }

        data.AsSpan(typeEnd).CopyTo(output.AsSpan(typeEnd - delta));
        for (var index = 0; index < SectionCount; index++)
        {
            var (offset, size) = ReadSection(data, index);
            if (index == TypeDefinitions)
            {
                WriteSection(output, index, offset, typeSize - delta);
                continue;
            }
            if (offset != 0 && offset < typeEnd && (offset >= typeOffset || offset + (long)size > typeOffset))
                throw new InvalidDataException($"Arknights_CN_Windows_2.7.91 metadata section {index} overlaps type definitions.");
            WriteSection(output, index, offset >= typeEnd ? offset - delta : offset, size);
        }
        return output;
    }

    private static void ValidateSections(ReadOnlySpan<byte> data)
    {
        for (var index = 0; index < SectionCount; index++)
        {
            var (offset, size) = ReadSection(data, index);
            if (
                size < 0
                || offset < 0
                || offset > data.Length
                || size > data.Length - offset
                || (offset != 0 && offset < ModifiedHeaderSize)
                || (offset == 0 && size != 0)
            )
                throw new InvalidDataException($"Arknights_CN_Windows_2.7.91 metadata section {index} is outside the payload.");
        }
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
