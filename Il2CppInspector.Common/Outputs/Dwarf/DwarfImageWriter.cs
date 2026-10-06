using System.Buffers.Binary;

namespace Il2CppInspector.Outputs.Dwarf;

internal static class DwarfImageWriter
{
    public static void Write(string path, int bits, Section[] mapped, IReadOnlyList<ElfDebugSection> sections, DwarfImageSource source)
    {
        var output = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(output, source.Path, comparison))
            throw new IOException("The DWARF output must not overwrite the input binary.");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536))
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(source.Read(0, 4)) == 0x464C457F)
                    ElfImageWriter.Write(file, bits, mapped, sections, source);
                else
                    MachOImageWriter.Write(file, bits, sections, source);
            }
            File.Move(temporary, output, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static void Align(Stream output, int alignment)
    {
        var position = checked((output.Position + alignment - 1) / alignment * alignment);
        if (position > output.Length)
            output.SetLength(position);
        output.Position = position;
    }

    public static void Copy(DwarfData data, Stream output)
    {
        data.Writer.Flush();
        data.Stream.Position = 0;
        data.Stream.CopyTo(output);
    }
}
