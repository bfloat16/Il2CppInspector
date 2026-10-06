using System.Buffers.Binary;

namespace Il2CppInspector.Outputs.Dwarf;

internal sealed class DwarfImageSource : IDisposable
{
    private readonly FileStream stream;
    private readonly long start;
    public long Length { get; }
    public string Path { get; }

    private DwarfImageSource(FileStream stream, string path, long start, long length)
    {
        this.stream = stream;
        Path = path;
        this.start = start;
        Length = length;
        CheckRange(0, length);
    }

    public static DwarfImageSource Open(string path, IFileFormatStream image)
    {
        var stream = File.OpenRead(path);
        try
        {
            var header = new byte[8];
            stream.ReadExactly(header);
            long offset = 0,
                length = stream.Length;
            var magic = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (magic is 0xCAFEBABE or 0xCAFEBABF)
            {
                var count = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
                var entrySize = magic == 0xCAFEBABE ? 20 : 32;
                var cpu = Cpu(image.Arch);
                var subtype = image.ReadUInt32(8);
                var found = false;
                for (var i = 0u; i < count; i++)
                {
                    stream.Position = checked(8L + i * entrySize);
                    var entry = new byte[entrySize];
                    stream.ReadExactly(entry);
                    if (BinaryPrimitives.ReadUInt32BigEndian(entry) != cpu || BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(4)) != subtype)
                        continue;
                    offset = checked((long)(entrySize == 20 ? BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(8)) : BinaryPrimitives.ReadUInt64BigEndian(entry.AsSpan(8))));
                    length = checked((long)(entrySize == 20 ? BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(12)) : BinaryPrimitives.ReadUInt64BigEndian(entry.AsSpan(16))));
                    found = true;
                    break;
                }
                if (!found)
                    throw new InvalidDataException("The input Mach-O has no matching architecture slice.");
            }
            var source = new DwarfImageSource(stream, System.IO.Path.GetFullPath(path), offset, length);
            if (length != image.Length)
                throw new InvalidDataException("The input binary size does not match the analyzed image.");
            header = source.Read(0, 20);
            var littleMagic = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (image.Format.StartsWith("ELF", StringComparison.Ordinal))
            {
                if (
                    littleMagic != 0x464C457F
                    || header[4] != (image.Bits == 64 ? 2 : 1)
                    || header[5] != 1
                    || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(18)) != ElfDebugWriter.Machine(image.Bits, image.Arch)
                )
                    throw new InvalidDataException("The input ELF does not match the analyzed image.");
            }
            else if (littleMagic != (image.Bits == 64 ? 0xFEEDFACFu : 0xFEEDFACEu) || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != Cpu(image.Arch))
                throw new InvalidDataException("The input Mach-O does not match the analyzed image.");
            return source;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static uint Cpu(string arch) =>
        arch switch
        {
            "x86" => 7,
            "x64" => 0x1000007,
            "ARM" => 12,
            "ARM64" => 0x100000C,
            _ => throw new NotSupportedException("Unsupported Mach-O architecture."),
        };

    private void CheckRange(long offset, long size)
    {
        if (offset < 0 || size < 0 || offset > Length - size || start < 0 || start > stream.Length - Length)
            throw new InvalidDataException("Binary file range lies outside the input image.");
    }

    public byte[] Read(long offset, int size)
    {
        CheckRange(offset, size);
        stream.Position = checked(start + offset);
        var result = new byte[size];
        stream.ReadExactly(result);
        return result;
    }

    public void CopyTo(Stream output, long offset = 0, long? size = null)
    {
        var remaining = size ?? Length - offset;
        CheckRange(offset, remaining);
        stream.Position = checked(start + offset);
        var buffer = new byte[65536];
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            stream.ReadExactly(buffer.AsSpan(0, count));
            output.Write(buffer.AsSpan(0, count));
            remaining -= count;
        }
    }

    public void Dispose() => stream.Dispose();
}
