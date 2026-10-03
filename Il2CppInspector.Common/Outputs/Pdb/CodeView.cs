using System.Buffers.Binary;
using System.Text;

namespace Il2CppInspector.Outputs.Pdb;

internal sealed class CvWriter : IDisposable
{
    private readonly MemoryStream stream = new();
    private readonly BinaryWriter writer;

    public CvWriter() => writer = new(stream, Encoding.UTF8, true);

    public int Length => checked((int)stream.Length);

    public void U8(byte value) => writer.Write(value);

    public void U16(ushort value) => writer.Write(value);

    public void U32(uint value) => writer.Write(value);

    public void U64(ulong value) => writer.Write(value);

    public void Bytes(ReadOnlySpan<byte> value) => writer.Write(value);

    public void String(string value)
    {
        Bytes(Encoding.UTF8.GetBytes(value));
        U8(0);
    }

    public void Align(bool type = false)
    {
        var padding = (4 - Length % 4) % 4;
        for (var i = padding; i > 0; i--)
            U8(type ? (byte)(0xF0 + i) : (byte)0);
    }

    public void Numeric(ulong value)
    {
        if (value < 0x8000)
            U16((ushort)value);
        else if (value <= ushort.MaxValue)
        {
            U16(0x8002);
            U16((ushort)value);
        }
        else if (value <= uint.MaxValue)
        {
            U16(0x8004);
            U32((uint)value);
        }
        else
        {
            U16(0x800A);
            U64(value);
        }
    }

    public byte[] ToArray() => stream.ToArray();

    public byte[] Record(ushort kind, bool type = false)
    {
        Align(type);
        using var result = new CvWriter();
        result.U16(checked((ushort)(Length + 2)));
        result.U16(kind);
        result.Bytes(stream.GetBuffer().AsSpan(0, Length));
        return result.ToArray();
    }

    public void Dispose()
    {
        writer.Dispose();
        stream.Dispose();
    }

    public static uint Hash(ReadOnlySpan<byte> bytes)
    {
        uint hash = 0;
        while (bytes.Length >= 4)
        {
            hash ^= BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            bytes = bytes[4..];
        }
        if (bytes.Length >= 2)
        {
            hash ^= BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            bytes = bytes[2..];
        }
        if (bytes.Length > 0)
            hash ^= bytes[0];
        hash |= 0x20202020;
        hash ^= hash >> 11;
        return hash ^ (hash >> 16);
    }

    private static readonly uint[] crcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value >> 1) ^ ((value & 1) != 0 ? 0xEDB88320u : 0);
            table[i] = value;
        }
        return table;
    }

    public static uint RecordHash(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0;
        foreach (var value in bytes)
            crc = crcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return crc;
    }
}

internal sealed record CvTypeRecord(byte[] Bytes, string Name = null, bool Forward = false);

internal sealed record PdbProcedure(string Name, ushort Segment, uint Offset, uint Rva, uint TypeIndex)
{
    public uint Size { get; set; }
}
