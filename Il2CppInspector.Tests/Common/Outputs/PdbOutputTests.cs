using System.Buffers.Binary;
using Il2CppInspector.Outputs.Pdb;

namespace Il2CppInspector.Tests.Common.Outputs;

internal static class PdbOutputTests
{
    internal static void Run(string directory, string llvmPdbUtil = null)
    {
        Directory.CreateDirectory(directory);
        var scalar = new CppType("float", 32);
        var vector = new CppComplexType(ComplexValueType.Struct) { Name = "Vector3" };
        foreach (var name in new[] { "x", "y", "z" })
            vector.AddField(name, scalar);
        var union = new CppComplexType(ComplexValueType.Union) { Name = "ValueUnion" };
        union.AddField("scalar", scalar);
        union.AddField("vector", vector);
        var large = new CppComplexType(ComplexValueType.Struct) { Name = "LargeFields" };
        for (var i = 0; i < 6000; i++)
            large.AddField("member_" + i, scalar);
        var enumeration = new CppEnumType(new CppType("int", 32)) { Name = "SignedEnum" };
        enumeration.AddField("Negative", -1);
        enumeration.AddField("Positive", 1);
        var flags = new CppComplexType(ComplexValueType.Struct) { Name = "Flags" };
        flags.AddField("ready", new CppType("uint32_t", 32), bitfield: 1);
        flags.AddField("mode", new CppType("uint32_t", 32), bitfield: 3);
        var types = new PdbTypes();
        var signature = new CppFnPtrType(64, scalar, [("value", vector.AsPointer(64)), ("array", vector.AsArray(4))]);
        var procedure = types.Procedure(signature);
        Check(types.Procedure(signature) == procedure, "Native PDB function signatures reuse their CodeView type records");
        types.Include([vector, union, large, enumeration, flags]);
        var sections = new byte[40];
        ".text"u8.CopyTo(sections);
        BinaryPrimitives.WriteUInt32LittleEndian(sections.AsSpan(8), 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(sections.AsSpan(12), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(sections.AsSpan(36), 0x60000020);
        var path = Path.Combine(directory, "native-fixture.pdb");
        var guid = new Guid("1b407efe-6052-4f34-8db7-b83c0cf13920");
        PdbStreams.Write(path, guid, 7, [new("Vector3_Length", 1, 0x20, 0x1020, procedure) { Size = 0x10 }], types.Records, sections);
        var bytes = File.ReadAllBytes(path);
        var streams = ReadStreams(bytes);
        Check(new Guid(streams[1].AsSpan(12, 16)) == guid && U32(streams[1], 8) == 7 && U32(streams[3], 8) == 7, "PDB info and DBI preserve the PE GUID and non-default age");
        Check(U32(streams[6], 4 + 4 + 12) == 0x10 && U32(streams[6], 4 + 4 + 24) == procedure, "Module procedure records retain code size and typed prototypes");
        Check(streams[8].Length > 16 && streams[9].Length > 28 && streams[10].Length > 0, "Native PDB emits global and public indices with shared symbol records");
        Pass("Native PDB fixture covers arrays, unions, enums, bitfields and field-list continuations");
        if (llvmPdbUtil != null)
        {
            var start = new System.Diagnostics.ProcessStartInfo(llvmPdbUtil)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "dump", "-summary", "-types", "-globals", "-global-name=Vector3_Length", "-symbols", path })
                start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var text = stdout.GetAwaiter().GetResult();
            Check(process.ExitCode == 0, "LLVM reads every native PDB fixture record: " + stderr.GetAwaiter().GetResult());
            Check(text.Contains("forward ref (-> 0x1003)") && text.Contains("S_PROCREF"), "LLVM resolves forward types and looks up global symbols by name using the PDB hashes");
            Check(
                text.Contains("member_5999") && text.Contains("LF_INDEX continuation") && text.Contains("LF_BITFIELD") && text.Contains("Negative = -1"),
                "LLVM reads field-list continuations, bitfields and signed enum values"
            );
        }
    }

    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static byte[][] ReadStreams(byte[] file)
    {
        var blockSize = checked((int)U32(file, 32));
        var directorySize = checked((int)U32(file, 44));
        var map = checked((int)U32(file, 52) * blockSize);
        var directory = new byte[directorySize];
        for (var offset = 0; offset < directory.Length; offset += blockSize)
            file.AsSpan(checked((int)U32(file, map + offset / blockSize * 4) * blockSize), Math.Min(blockSize, directory.Length - offset)).CopyTo(directory.AsSpan(offset));
        var streams = new byte[U32(directory, 0)][];
        var blockIndex = 4 + streams.Length * 4;
        for (var i = 0; i < streams.Length; i++)
        {
            streams[i] = new byte[U32(directory, 4 + i * 4)];
            for (var offset = 0; offset < streams[i].Length; offset += blockSize)
            {
                file.AsSpan(checked((int)U32(directory, blockIndex) * blockSize), Math.Min(blockSize, streams[i].Length - offset)).CopyTo(streams[i].AsSpan(offset));
                blockIndex += 4;
            }
        }
        return streams;
    }
}
