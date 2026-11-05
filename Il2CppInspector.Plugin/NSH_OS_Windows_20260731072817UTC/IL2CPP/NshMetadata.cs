using System.Buffers.Binary;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector.Plugin.NSH;

internal static class NshMetadata
{
    // NSH v27 places the first string literal at 0xAB0 instead of the stock 0x100-byte header boundary.
    private const int HeaderSize = 0xAB0;
    private const int StockHeaderSize = 0x100;
    private const int HeaderDelta = HeaderSize - StockHeaderSize;

    internal static byte[] Normalize(ReadOnlySpan<byte> encrypted)
    {
        var data = encrypted.ToArray();
        Decrypt(data);

        // 0x180C57710 reads images/assemblies, 0x180C54340 types, 0x180C4D270 methods/parameters,
        // 0x180C4CE80 fields, 0x180C4DE50 events and 0x180C4E190 properties. Records retain stock v27 order.
        (int Offset, int Size, int Stride)[] sections =
        [
            Section(0xC0, 0x654, 8),
            Section(0x3A8, 0x6D0, 1),
            Section(0x9F4, 0x4C4, 1),
            Section(0x148, 0x8C4, 24),
            Section(0x590, 0x6DC, 20),
            Section(0x3A0, 0x274, 32),
            Section(0x2C0, 0x6FC, 12),
            Section(0x59C, 0x64C, 12),
            Section(0x33C, 0x8FC, 1),
            (Read(0x33C) + Read(0x8FC), Read(0x5AC), 12),
            Section(0xA54, 0x7E4, 12),
            Section(0x8AC, 0x7F4, 12),
            Section(0x3C0, 0x448, 16),
            Section(0xF8, 0x18, 4),
            Section(0x898, 0x57C, 16),
            Section(0x998, 0x3AC, 4),
            Section(0x39C, 0x390, 4),
            Section(0xA1C, 0x428, 4),
            Section(0x878, 0xAAC, 8),
            Section(0x9C4, 0x968, 88),
            Section(0x30, 0x880, 40),
            Section(0x100, 0x574, 64),
            (Read(0x800), Read(0x6C) - Read(0x800), 8),
            (Read(0x6C), Read(0x3DC) - Read(0x6C), 4),
            Section(0x3DC, 0x818, 12),
            Section(0x4, 0xA00, 4),
            Section(0x438, 0xA98, 4),
            Section(0x8BC, 0x320, 8),
            Section(0x244, 0x6AC, 8),
            Section(0x208, 0x6AC, 1),
            Section(0x9D4, 0x714, 4),
        ];

        // Reconstruct marshaled-size and referenced-assembly extents from their neighboring tables.
        for (var i = 0; i < sections.Length; i++)
        {
            var (offset, size, stride) = sections[i];
            if (offset < HeaderSize || offset > data.Length || size < 0 || size > data.Length - offset || size % stride != 0)
                throw new InvalidDataException($"NSH metadata section {i} has an invalid range or record size.");
        }
        var end = HeaderSize;
        foreach (var section in sections.Where(static s => s.Size != 0).OrderBy(static s => s.Offset))
        {
            if (section.Offset < end)
                throw new InvalidDataException($"NSH metadata section at 0x{section.Offset:X} overlaps another section.");
            end = section.Offset + section.Size;
        }
        if (sections[0].Offset != HeaderSize || end != data.Length)
            throw new InvalidDataException("NSH metadata sections do not cover the expected payload boundaries.");

        var output = new byte[data.Length - HeaderDelta];
        data.AsSpan(HeaderSize).CopyTo(output.AsSpan(StockHeaderSize));
        BinaryPrimitives.WriteInt32LittleEndian(output, Il2CppGlobalMetadataHeader.ExpectedSanity);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), 27);
        for (var i = 0; i < sections.Length; i++)
        {
            var entry = output.AsSpan(8 + i * 8, 8);
            BinaryPrimitives.WriteInt32LittleEndian(entry, sections[i].Offset - HeaderDelta);
            BinaryPrimitives.WriteInt32LittleEndian(entry[4..], sections[i].Size);
        }

        // 0x180C535B0 resolves {length, offset} pairs and 0x180C532A0 restarts the XOR key for each literal.
        var key = "85b64fc6fbb64458a70b91c4f656a358"u8;
        var literals = output.AsSpan(sections[0].Offset - HeaderDelta, sections[0].Size);
        var blob = output.AsSpan(sections[1].Offset - HeaderDelta, sections[1].Size);
        var literalEnd = 0;
        for (var i = 0; i < literals.Length / 8; i++)
        {
            var entry = literals.Slice(i * 8, 8);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
            if (offset != literalEnd || length < 0 || length > blob.Length - offset)
                throw new InvalidDataException($"NSH string literal {i} has an invalid data range.");
            for (var j = 0; j < length; j++)
                blob[offset + j] ^= key[j % key.Length];
            literalEnd += length;
        }
        return output;

        int Read(int slot) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(slot, 4));

        (int Offset, int Size, int Stride) Section(int offsetSlot, int sizeSlot, int stride) => (Read(offsetSlot), Read(sizeSlot), stride);
    }

    private static void Decrypt(Span<byte> data)
    {
        // 0x180C59B8B..0x180C59E2C: RC4 KSA/PRGA with a continuous state across 0x1000-byte chunks.
        var key = "90f5d348794b46d9a98b24ee3ff57eec"u8;
        Span<byte> state = stackalloc byte[256];
        for (var i = 0; i < state.Length; i++)
            state[i] = (byte)i;
        var j = 0;
        for (var i = 0; i < state.Length; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 255;
            (state[i], state[j]) = (state[j], state[i]);
        }
        var x = 0;
        j = 0;
        for (var i = 0; i < data.Length; i++)
        {
            x = (x + 1) & 255;
            j = (j + state[x]) & 255;
            (state[x], state[j]) = (state[j], state[x]);
            data[i] ^= state[(state[x] + state[j]) & 255];
        }
    }
}
