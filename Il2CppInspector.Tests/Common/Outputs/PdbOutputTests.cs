using System.Buffers.Binary;
using Il2CppInspector.Outputs.Pdb;

namespace Il2CppInspector.Tests.Common.Outputs;

internal static class PdbOutputTests
{
    internal static void Run(string directory, string llvmPdbUtil = null, string idat = null)
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
        var narrowEnum = new CppEnumType(new CppType("uint8_t", 8)) { Name = "ByteMode" };
        var wideEnum = new CppEnumType(new CppType("int32_t", 32)) { Name = "IntMode" };
        foreach (var mode in new[] { narrowEnum, wideEnum })
        {
            mode.AddField("Off", 0);
            mode.AddField("On", 1);
        }
        var flags = new CppComplexType(ComplexValueType.Struct) { Name = "Flags" };
        flags.AddField("ready", new CppType("uint32_t", 32), bitfield: 1);
        flags.AddField("mode", new CppType("uint32_t", 32), bitfield: 15);
        flags.AddField("exponent", new CppType("uint32_t", 32), bitfield: 8);
        flags.AddField("sign", new CppType("uint32_t", 32), bitfield: 8);
        flags.AddField("next", new CppType("uint32_t", 32), bitfield: 1);
        var types = new PdbTypes();
        var qualified = new CppComplexType(ComplexValueType.Struct) { Name = "QualifiedHolder" };
        qualified.AddField("pointer", vector.AsConst().AsPointer(64));
        qualified.AddField("value", new CppVolatileType(new CppType("int", 32)));
        qualified.AddField("byte", new CppType("uint8_t", 8));
        var signature = new CppFnPtrType(64, scalar, [("value", vector.AsPointer(64)), ("array", vector.AsArray(4))]);
        var procedure = types.Procedure(signature);
        Check(types.Procedure(signature) == procedure, "Native PDB function signatures reuse their CodeView type records");
        types.Include([vector, union, large, enumeration, flags, qualified, narrowEnum, wideEnum]);
        var flagRecord = types.Records.Single(r => r.Name == "Flags" && !r.Forward);
        var fieldList = types.Records[checked((int)U32(flagRecord.Bytes, 8) - 0x1000)].Bytes;
        var bitFields = ReadFields(fieldList);
        Check(bitFields.Select(f => f.Offset).SequenceEqual([0, 0, 0, 0, 4]), "CodeView bitfields use storage-unit byte offsets, not individual field byte offsets");
        Check(
            bitFields.Select(f => types.Records[checked((int)f.Type - 0x1000)].Bytes[9]).SequenceEqual(new byte[] { 0, 1, 16, 24, 0 }),
            "CodeView bit positions remain relative to their storage units across byte and unit boundaries"
        );
        Check(
            enumeration.ToString("cb").Contains("enum SignedEnum : int") && enumeration.ToString("cb").Contains("SignedEnum_Negative"),
            "Clang enum declarations retain their explicit storage type and globally unique member names"
        );
        Check(qualified.SizeBytes == 16 && types.Records.Count(r => BitConverter.ToUInt16(r.Bytes, 2) == 0x1001) >= 2, "Native PDB preserves tail padding and emits const/volatile modifier records");
        var sections = new byte[80];
        ".text"u8.CopyTo(sections);
        BinaryPrimitives.WriteUInt32LittleEndian(sections.AsSpan(8), 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(sections.AsSpan(12), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(sections.AsSpan(36), 0x60000020);
        ".data"u8.CopyTo(sections.AsSpan(40));
        BinaryPrimitives.WriteUInt32LittleEndian(sections.AsSpan(48), 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(sections.AsSpan(52), 0x2000);
        BinaryPrimitives.WriteUInt32LittleEndian(sections.AsSpan(76), 0xC0000040);
        var path = Path.Combine(directory, "native-fixture.pdb");
        var guid = new Guid("1b407efe-6052-4f34-8db7-b83c0cf13920");
        var globals = new PdbGlobal[]
        {
            new("GlobalVector", 2, 0x10, types.Resolve(vector)),
            new("GlobalVectorPointer", 2, 0x20, types.Resolve(vector.AsPointer(64))),
            new("GlobalVectorArray", 2, 0x30, types.Resolve(vector.AsArray(4))),
        };
        PdbStreams.Write(path, guid, 7, [new("Vector3_Length", 1, 0x20, 0x1020, procedure) { Size = 0x10 }], types.Records, sections, globals);
        var bytes = File.ReadAllBytes(path);
        var streams = ReadStreams(bytes);
        Check(new Guid(streams[1].AsSpan(12, 16)) == guid && U32(streams[1], 8) == 7 && U32(streams[3], 8) == 7, "PDB info and DBI preserve the PE GUID and non-default age");
        var procedureOffset = 4;
        while (procedureOffset < streams[6].Length && BinaryPrimitives.ReadUInt16LittleEndian(streams[6].AsSpan(procedureOffset + 2)) != 0x1110)
            procedureOffset += BinaryPrimitives.ReadUInt16LittleEndian(streams[6].AsSpan(procedureOffset)) + 2;
        Check(
            procedureOffset < streams[6].Length && U32(streams[6], procedureOffset + 4 + 12) == 0x10 && U32(streams[6], procedureOffset + 4 + 24) == procedure,
            "Module procedure records retain code size and typed prototypes"
        );
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
            foreach (var argument in new[] { "dump", "-summary", "-types", "-globals", "-global-name=Vector3_Length", "-global-name=GlobalVector", "-symbols", path })
                start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var text = stdout.GetAwaiter().GetResult();
            Check(process.ExitCode == 0, "LLVM reads every native PDB fixture record: " + stderr.GetAwaiter().GetResult());
            Check(text.Contains("forward ref (-> 0x1003)") && text.Contains("S_PROCREF"), "LLVM resolves forward types and looks up global symbols by name using the PDB hashes");
            Check(text.Contains("S_GDATA32") && text.Contains("GlobalVector"), "LLVM resolves typed global data records through the PDB global symbol index");
            Check(
                text.Contains("member_5999") && text.Contains("LF_INDEX continuation") && text.Contains("LF_BITFIELD") && text.Contains("SignedEnum_Negative = -1"),
                "LLVM reads field-list continuations, bitfields and signed enum values"
            );
        }
        if (idat != null)
            PdbIdaTests.Run(directory, path, guid, 7, idat);
    }

    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static List<(uint Type, int Offset)> ReadFields(byte[] record)
    {
        var result = new List<(uint, int)>();
        var cursor = 4;
        while (cursor < record.Length)
        {
            if (record[cursor] >= 0xF0)
            {
                cursor++;
                continue;
            }
            if (BitConverter.ToUInt16(record, cursor) != 0x150D)
                throw new InvalidDataException("Unexpected CodeView field-list leaf.");
            result.Add((U32(record, cursor + 4), BitConverter.ToUInt16(record, cursor + 8)));
            cursor = Array.IndexOf(record, (byte)0, cursor + 10) + 1;
        }
        return result;
    }

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
