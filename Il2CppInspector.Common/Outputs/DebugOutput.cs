namespace Il2CppInspector.Outputs;

public enum DebugSymbolFormat
{
    Pdb,
    Dwarf,
}

public static class DebugOutput
{
    public static DebugSymbolFormat SelectFormat(IFileFormatStream image)
    {
        if (image.Format.StartsWith("PE", StringComparison.Ordinal))
        {
            if (!PdbOutput.Supports(image))
                throw new NotSupportedException($"Debug output for {image.Format} ({image.Arch}) requires an x64 PE binary for PDB generation.");
            return DebugSymbolFormat.Pdb;
        }
        if (image.Format.StartsWith("ELF", StringComparison.Ordinal) || image.Format.StartsWith("Mach-O", StringComparison.Ordinal))
        {
            if (!DwarfOutput.Supports(image))
                throw new NotSupportedException($"Debug output for {image.Format} ({image.Arch}) requires a little-endian x86, x64, ARM or ARM64 image for DWARF generation.");
            return DebugSymbolFormat.Dwarf;
        }
        throw new NotSupportedException($"Debug output does not support binary format {image.Format} ({image.Arch}).");
    }
}
