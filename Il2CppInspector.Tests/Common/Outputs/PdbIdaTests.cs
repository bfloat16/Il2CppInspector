using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace Il2CppInspector.Tests.Common.Outputs;

internal static class PdbIdaTests
{
    internal static void Run(string directory, string pdb, Guid guid, uint age, string idat)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Il2CppInspector.slnx")))
            root = root.Parent;
        if (root == null)
            throw new DirectoryNotFoundException("Cannot locate IDA test fixtures.");
        var fixtures = Path.Combine(root.FullName, "Il2CppInspector.Tests", "Common", "Outputs", "Fixtures");
        var input = Path.Combine(directory, "pdb-target.exe");
        DwarfIdaTests.RunTool(
            "clang",
            [
                "--target=x86_64-pc-windows-msvc",
                "-nostdlib",
                "-fuse-ld=lld",
                "-Wl,/entry:_start,/subsystem:console,/debug,/nodefaultlib,/pdb:" + Path.Combine(directory, "original-pdb-identity.pdb"),
                Path.Combine(fixtures, "PdbIdaFixture.s"),
                "-o",
                input,
            ]
        );
        var bytes = File.ReadAllBytes(input);
        using (var reader = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(bytes)))
        {
            var entry = reader.ReadDebugDirectory().Single(e => e.Type == DebugDirectoryEntryType.CodeView);
            var offset = entry.DataPointer;
            if (!bytes.AsSpan(offset, 4).SequenceEqual("RSDS"u8))
                throw new InvalidDataException("Expected RSDS identity in the generated PE fixture.");
            guid.TryWriteBytes(bytes.AsSpan(offset + 4, 16));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 20), age);
            var name = System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(pdb));
            if (name.Length + 1 > entry.DataSize - 24)
                throw new InvalidDataException("PDB fixture path exceeds the reserved CodeView path.");
            bytes.AsSpan(offset + 24, entry.DataSize - 24).Clear();
            name.CopyTo(bytes.AsSpan(offset + 24));
        }
        File.WriteAllBytes(input, bytes);
        var log = Path.Combine(directory, "ida-pdb.log");
        DwarfIdaTests.RunTool(idat, ["-A", "-a", "-c", "-Opdb:pdbida", "-L" + log, "-o" + Path.Combine(directory, "pdb-target.i64"), "-S" + Path.Combine(fixtures, "VerifyPdbInIda.py"), input]);
        var line = File.ReadLines(log).Last(l => l.StartsWith("PDB_IDA_RESULT=", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(line["PDB_IDA_RESULT=".Length..]);
        var result = json.RootElement;
        Check(
            result.GetProperty("functionName").GetString() == "Vector3_Length" && result.GetProperty("functionType").GetString()?.Contains("Vector3") == true,
            "IDA imports PDB function names and structure-bearing signatures"
        );
        Check(result.GetProperty("flagOffsets").EnumerateArray().Select(o => o.GetInt32()).SequenceEqual([0, 1, 16, 24, 32]), "IDA retains every PDB bitfield at the correct absolute bit position");
        Check(result.GetProperty("enumWidths").EnumerateArray().Select(o => o.GetInt32()).SequenceEqual([1, 4]), "IDA keeps equal-valued enums with distinct underlying widths separate");
        Check(
            result.GetProperty("globalSizes").EnumerateArray().Select(o => o.GetInt32()).SequenceEqual([12, 8, 48])
                && result.GetProperty("globalTypes").EnumerateArray().All(t => t.GetString()?.Contains("Vector3") == true),
            "IDA applies PDB global structure, pointer and array types without a supplement script"
        );
    }
}
