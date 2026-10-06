namespace Il2CppInspector.Outputs.Dwarf;

internal sealed class ElfDebugSymbols : IDisposable
{
    public DwarfData Locals { get; } = new();
    public DwarfData Globals { get; } = new();
    public uint LocalCount { get; }
    public uint GlobalCount { get; }

    public ElfDebugSymbols(DwarfData symbols, int bits)
    {
        try
        {
            var entrySize = bits == 64 ? 24 : 16;
            var bindingOffset = bits == 64 ? 4 : 12;
            symbols.Writer.Flush();
            if (symbols.Stream.Length < entrySize || symbols.Stream.Length % entrySize != 0)
                throw new InvalidDataException("Invalid generated ELF symbol table.");
            symbols.Stream.Position = 0;
            var record = new byte[entrySize];
            symbols.Stream.ReadExactly(record);
            if (record.Any(b => b != 0))
                throw new InvalidDataException("ELF symbol table must start with an undefined symbol.");
            Locals.Writer.Write(record);
            while (symbols.Stream.Position < symbols.Stream.Length)
            {
                symbols.Stream.ReadExactly(record);
                var target = record[bindingOffset] >> 4 == 0 ? Locals : Globals;
                target.Writer.Write(record);
            }
            LocalCount = checked((uint)(Locals.Stream.Length / entrySize));
            GlobalCount = checked((uint)(Globals.Stream.Length / entrySize));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void CopyTo(Stream output)
    {
        DwarfImageWriter.Copy(Locals, output);
        DwarfImageWriter.Copy(Globals, output);
    }

    public void Dispose()
    {
        Locals.Dispose();
        Globals.Dispose();
    }
}
