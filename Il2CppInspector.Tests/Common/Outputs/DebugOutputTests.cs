namespace Il2CppInspector.Tests.Common.Outputs;

internal static class DebugOutputTests
{
    private sealed class TestImage(string format, int bits, string arch) : FileFormatStream<TestImage>
    {
        public override string DefaultFilename => "misleading-extension.dll";
        public override string Format => format;
        public override string Arch => arch;
        public override int Bits { get; set; } = bits;
    }

    internal static void Run()
    {
        foreach (
            var (format, bits, arch, expected) in new[]
            {
                ("PE32+", 64, "x64", DebugSymbolFormat.Pdb),
                ("ELF64", 64, "x64", DebugSymbolFormat.Dwarf),
                ("ELF64", 64, "ARM64", DebugSymbolFormat.Dwarf),
                ("ELF", 32, "x86", DebugSymbolFormat.Dwarf),
                ("ELF", 32, "ARM", DebugSymbolFormat.Dwarf),
                ("Mach-O 64-bit", 64, "x64", DebugSymbolFormat.Dwarf),
                ("Mach-O 64-bit", 64, "ARM64", DebugSymbolFormat.Dwarf),
                ("Mach-O 32-bit", 32, "x86", DebugSymbolFormat.Dwarf),
                ("Mach-O 32-bit", 32, "ARM", DebugSymbolFormat.Dwarf),
            }
        )
        {
            using var image = new TestImage(format, bits, arch);
            Check(DebugOutput.SelectFormat(image) == expected, $"Debug routes {format} {arch} to {expected} independently of filename extension");
        }
        Reject("PE32", 32, "x86", "x64 PE");
        Reject("PE32+", 64, "ARM64", "x64 PE");
        Reject("ELF64", 64, "Unsupported", "little-endian");
        Reject("Mach-O 64-bit", 32, "ARM64", "little-endian");
        Reject("WASM", 64, "x64", "does not support binary format");
        Reject("ELF64", 64, "ARM64", "little-endian", Bin2Object.Endianness.Big);

        static void Reject(string format, int bits, string arch, string message, Bin2Object.Endianness endian = Bin2Object.Endianness.Little)
        {
            using var image = new TestImage(format, bits, arch) { Endianness = endian };
            try
            {
                DebugOutput.SelectFormat(image);
            }
            catch (NotSupportedException ex)
            {
                Check(ex.Message.Contains(message), $"Debug rejects unsupported {format} {arch} ({endian}) without routing to an inappropriate symbol format");
                return;
            }
            throw new InvalidOperationException($"Debug unexpectedly accepted {format} {arch} ({endian}).");
        }
    }
}
