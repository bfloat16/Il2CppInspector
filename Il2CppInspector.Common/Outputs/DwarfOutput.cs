using Bin2Object;
using Il2CppInspector.Model;
using Il2CppInspector.Next;
using Il2CppInspector.Outputs.Dwarf;

namespace Il2CppInspector.Outputs;

public sealed record DwarfOutputResult(int Functions, int TypedFunctions, int TypeRecords, int CompilationUnits)
{
    public int Globals { get; init; }
}

public sealed class DwarfOutput(AppModel model)
{
    public static bool Supports(IFileFormatStream image) =>
        (image.Bits, image.Arch) is (32, "x86") or (64, "x64") or (32, "ARM") or (64, "ARM64") && image is BinaryObjectStreamReader { Endianness: Endianness.Little };

    // GNU debuglink lookup requires both the stored file name and a matching CRC.
    public static string GetCompanionName(IFileFormatStream image, string fallback) => ElfDebugLink.GetName(image) ?? fallback;

    public static string CreateDebugLinkImage(IFileFormatStream image, string inputFile, string debugFile) => ElfDebugLink.CreateImage(image, inputFile, debugFile);

    public DwarfOutputResult Write(string outputFile, EventHandler<string> status = null) => WriteCore(outputFile, status, null);

    public DwarfOutputResult WriteImage(string outputFile, string inputFile, EventHandler<string> status = null)
    {
        var image = model.Package.BinaryImage;
        if (!Supports(image))
            throw new NotSupportedException("DWARF output requires a little-endian x86, x64, ARM or ARM64 image.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFullPath(outputFile), Path.GetFullPath(inputFile), comparison))
            throw new IOException("The DWARF output must not overwrite the input binary.");
        var position = image.Position;
        try
        {
            using var source = DwarfImageSource.Open(inputFile, image);
            return WriteCore(outputFile, status, source);
        }
        finally
        {
            image.Position = position;
        }
    }

    private DwarfOutputResult WriteCore(string outputFile, EventHandler<string> status, DwarfImageSource source)
    {
        var image = model.Package.BinaryImage;
        if (!Supports(image))
            throw new NotSupportedException("DWARF output requires a little-endian x86, x64, ARM or ARM64 image.");
        var position = image.Position;
        try
        {
            var sections = image.GetSections().Where(s => (s.IsExec || s.IsData || s.IsBSS) && s.VirtualEnd >= s.VirtualStart && s.VirtualLength != 0).OrderBy(s => s.VirtualStart).ToArray();
            var code = sections.Where(s => s.IsExec).ToArray();
            var data = sections.Where(s => !s.IsExec).ToArray();
            if (code.Length == 0)
                throw new InvalidDataException("DWARF output requires mapped executable sections.");
            var boundaries = model.Package.FunctionAddresses.Keys.Concat(model.EnumerateNativeMethods().Select(m => m.Address));
            using var document = new DwarfDocument(image.Bits, image.Arch, image.DefaultFilename, code, boundaries, dataSections: data);
            foreach (var method in model.EnumerateNativeMethods())
                document.Method(method);
            document.Write(outputFile, model.EnumerateNativeTypes(), NativeDebugGlobals.Enumerate(model), source);
            status?.Invoke(this, $"DWARF functions: {document.Functions} ({document.TypedFunctions} typed) in {document.CompilationUnits} units");
            status?.Invoke(this, $"DWARF type records: {document.TypeRecords}");
            status?.Invoke(this, $"DWARF globals: {document.Globals}");
            return new(document.Functions, document.TypedFunctions, document.TypeRecords, document.CompilationUnits) { Globals = document.Globals };
        }
        finally
        {
            image.Position = position;
        }
    }
}
