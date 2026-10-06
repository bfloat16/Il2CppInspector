using System.Security.Cryptography;
using Il2CppInspector.Outputs.Dwarf;

namespace Il2CppInspector.Tests.Common.Outputs;

internal static class DwarfGlobalTests
{
    internal static void Run(string sample, string directory, string idat = null)
    {
        Directory.CreateDirectory(directory);
        var input = Path.Combine(sample, "libil2cpp.so");
        var originalHash = SHA256.HashData(File.ReadAllBytes(input));
        var package = Inspector.LoadFromFile(input, Path.Combine(sample, "global-metadata.dat"))?.Single() ?? throw new InvalidDataException("Cannot load the Android DWARF global fixture.");
        var model = new AppModel(new TypeModel(package));
        var debugFile = Path.Combine(directory, DwarfOutput.GetCompanionName(model.Image, "libil2cpp.sym.so"));
        var result = new DwarfOutput(model).Write(debugFile);
        Check(result.Globals > 0, "Android DWARF contains address-bearing global definitions");
        var sections = model.Image.GetSections().Where(s => !s.IsExec && (s.IsData || s.IsBSS)).ToArray();
        var names = new HashSet<string>();
        var globals = NativeDebugGlobals
            .Enumerate(model)
            .Where(g =>
                g.Type != null
                && g.Address != 0
                && g.Address != ulong.MaxValue
                && g.Type.SizeBytes > 0
                && sections.Any(s => g.Address >= s.VirtualStart && g.Address <= s.VirtualEnd && (ulong)g.Type.SizeBytes <= s.VirtualEnd - g.Address + 1)
            )
            .DistinctBy(g => g.Address)
            .Select(g =>
            {
                var name = g.Name;
                while (!names.Add(name))
                    name += $"_{g.Address:X}";
                var type = g.Type;
                int count = 0;
                if (type is CppArrayType array)
                {
                    count = array.Length;
                    type = array.ElementType;
                }
                bool pointer = type is CppPointerType;
                if (type is CppPointerType ptr)
                    type = ptr.ElementType;
                return new
                {
                    address = g.Address,
                    name,
                    size = g.Type.SizeBytes,
                    pointer,
                    count,
                    type = type.Name,
                    fields = !pointer && type is CppComplexType structure ? structure.Fields.Values.SelectMany(f => f).Select(f => new { name = f.Name, offset = f.OffsetBytes }).ToArray() : [],
                };
            })
            .ToArray();
        Check(globals.Length == result.Globals, "Every recognized mapped data address has one DWARF global");
        Check(
            globals.Single(g => g.name == "g_CodeRegistration").address == package.Binary.CodeRegistrationPointer
                && globals.Single(g => g.name == "g_MetadataRegistration").address == package.Binary.MetadataRegistrationPointer,
            "Registration structures use the reader's actual object addresses"
        );
        Check(globals.Single(g => g.name == "g_CodeGenModules").count == package.Binary.Modules.Count, "CodeGenModule pointer array has the reader's actual module count");
        var manifest = Path.Combine(directory, "globals.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { globals }));
        var patched = DwarfOutput.CreateDebugLinkImage(model.Image, input, debugFile);
        Check(patched != null && SHA256.HashData(File.ReadAllBytes(input)).SequenceEqual(originalHash), "Global DWARF debuglink repair leaves the sample ELF unchanged");
        Console.WriteLine($"DWARF_GLOBAL_OUTPUT={debugFile}; functions={result.Functions}; globals={result.Globals}; units={result.CompilationUnits}");
        if (idat == null)
            return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Il2CppInspector.slnx")))
            root = root.Parent;
        if (root == null)
            throw new DirectoryNotFoundException("Cannot locate the DWARF global probe.");
        var script = Path.Combine(root.FullName, "Il2CppInspector.Tests", "Common", "Outputs", "Fixtures", "VerifyDwarfGlobalsInIda.py");
        var log = Path.Combine(directory, "ida-globals.log");
        // Disable whole-image autoanalysis: this test checks the DWARF import itself.
        DwarfIdaTests.RunTool(idat, ["-A", "-a", "-c", "-L" + log, "-o" + Path.Combine(directory, "unity-globals.i64"), "-S\"" + script + "\" \"" + manifest + "\"", patched]);
        var report = File.ReadLines(log).Last(line => line.StartsWith("DWARF_GLOBAL_IDA_RESULT=", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(report["DWARF_GLOBAL_IDA_RESULT=".Length..]);
        var summary = json.RootElement;
        Console.WriteLine(report);
        Check(
            summary.GetProperty("checked").GetInt32() == globals.Length && summary.GetProperty("failed").GetInt32() == 0,
            "IDA applies every Android global's type, pointer depth, array length, size and structure field offsets without supplementary scripts"
        );
        Check(!File.ReadAllText(log).Contains("DWARF: Globals: 0 symbols applied"), "IDA reports applied global symbols for the Android sample");
    }
}
