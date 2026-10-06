using System.Buffers.Binary;
using System.Text;

namespace Il2CppInspector.Outputs.Dwarf;

internal static class ElfDebugLink
{
    private sealed record Link(string Name, Section Section, int CrcOffset);

    private static readonly uint[] CrcTable = BuildCrcTable();

    public static string GetName(IFileFormatStream image) => Read(image)?.Name;

    public static string CreateImage(IFileFormatStream image, string inputFile, string debugFile)
    {
        var link = Read(image);
        if (link == null)
            return null;
        if (!string.Equals(link.Name, Path.GetFileName(debugFile), StringComparison.Ordinal))
            throw new ArgumentException("The debug companion must have the name stored in .gnu_debuglink.", nameof(debugFile));

        using var source = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = new byte[20];
        if (source.Length < header.Length || source.Length != image.Length)
            return null;
        source.ReadExactly(header);
        if (
            !header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' })
            || header[4] != (image.Bits == 64 ? 2 : 1)
            || header[5] != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(18)) != ElfDebugWriter.Machine(image.Bits, image.Arch)
        )
            return null;

        source.Position = link.Section.ImageStart;
        var originalLink = new byte[checked((int)link.Section.ImageLength)];
        source.ReadExactly(originalLink);
        if (!TryParse(originalLink, out var name, out var crcOffset) || name != link.Name || crcOffset != link.CrcOffset)
            return null;

        var directory = Path.GetDirectoryName(Path.GetFullPath(debugFile));
        var output = Path.Combine(directory, Path.GetFileNameWithoutExtension(inputFile) + ".debuglink" + Path.GetExtension(inputFile));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFullPath(inputFile), output, comparison) || string.Equals(Path.GetFullPath(debugFile), output, comparison))
            throw new IOException("The debuglink image must not overwrite the input or debug companion.");

        var crc = ComputeCrc(debugFile);
        using var target = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None);
        source.Position = 0;
        source.CopyTo(target);
        target.Position = checked(link.Section.ImageStart + link.CrcOffset);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(checksum, crc);
        target.Write(checksum);
        return output;
    }

    private static Link Read(IFileFormatStream image)
    {
        if (!image.Format.StartsWith("ELF", StringComparison.Ordinal) || !image.TryGetSections(out var sections))
            return null;
        var section = sections.FirstOrDefault(s => s.Name == ".gnu_debuglink");
        if (section == null || section.ImageLength < 8 || section.ImageLength > 65536 || section.ImageStart > image.Length - section.ImageLength)
            return null;
        var position = image.Position;
        try
        {
            image.Position = section.ImageStart;
            return TryParse(image.ReadBytes(checked((int)section.ImageLength)), out var name, out var offset) ? new(name, section, offset) : null;
        }
        finally
        {
            image.Position = position;
        }
    }

    private static bool TryParse(byte[] data, out string name, out int crcOffset)
    {
        name = null;
        crcOffset = 0;
        var end = Array.IndexOf(data, (byte)0);
        if (end <= 0)
            return false;
        crcOffset = (end + 1 + 3) & ~3;
        if (crcOffset > data.Length - 4)
            return false;
        name = Encoding.UTF8.GetString(data, 0, end);
        return !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) && !name.Contains('/') && !name.Contains('\\');
    }

    private static uint ComputeCrc(string file)
    {
        // GNU debuglink uses the standard reflected CRC-32, as llvm-objcopy does.
        using var stream = File.OpenRead(file);
        var buffer = new byte[65536];
        uint crc = uint.MaxValue;
        int count;
        while ((count = stream.Read(buffer)) != 0)
            for (var i = 0; i < count; i++)
                crc = CrcTable[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320u);
            table[i] = value;
        }
        return table;
    }
}
