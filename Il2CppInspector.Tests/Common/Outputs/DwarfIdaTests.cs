namespace Il2CppInspector.Tests.Common.Outputs;

internal static class DwarfIdaTests
{
    internal static void Run(string directory, string symbols, string idat)
    {
        var input = Path.Combine(directory, "ida-target.elf");
        var debugFile = Path.Combine(directory, "ida-target.sym");
        File.WriteAllText(debugFile, "stale debug companion");
        TestEnvironment.RunTool(
            "clang",
            [
                "--target=aarch64-linux-gnu",
                "-nostdlib",
                "-fuse-ld=lld",
                "-Wl,-Ttext=0x140001000",
                "-Wl,-Tdata=0x140002000",
                "-Wl,--section-start=.bss=0x140003000",
                "-Wl,--entry=_start",
                TestEnvironment.FixturePath("DwarfIdaFixture.s"),
                "-o",
                input,
            ]
        );
        TestEnvironment.RunTool("llvm-objcopy", ["--strip-all", input]);
        TestEnvironment.RunTool("llvm-objcopy", ["--add-gnu-debuglink=" + debugFile, input]);
        File.Copy(symbols, debugFile, true);
        var image = FileFormatStream.Load(input);
        string patched;
        using ((IDisposable)image)
            patched = DwarfOutput.CreateDebugLinkImage(image, input, debugFile);
        var log = Path.Combine(directory, "ida-dwarf.log");
        TestEnvironment.RunTool(
            idat,
            [
                "-A",
                "-c",
                "-L" + log,
                "-o" + Path.Combine(directory, "ida-target.i64"),
                "-S" + TestEnvironment.FixturePath("VerifyDwarfInIda.py"),
                patched,
            ]
        );
        var report = File.ReadLines(log).Last(line => line.StartsWith("DWARF_IDA_RESULT=", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(report["DWARF_IDA_RESULT=".Length..]);
        var result = json.RootElement;
        var functions = result.GetProperty("functions").EnumerateArray().ToArray();
        var function = functions.Single(f => f.GetProperty("address").GetString() == "0x140001020");
        Check(
            function.GetProperty("name").GetString() is "Vector3_Length" or "SharedAddress",
            "IDA imports a function name from the standalone .sym file, not from the stripped ELF"
        );
        Check(function.GetProperty("type").GetString()?.Contains("Vector3") == true, "IDA imports the DWARF function prototype and argument types");
        Check(
            result.GetProperty("vector_size").GetUInt64() == 12 && result.GetProperty("types").EnumerateArray().Any(t => t.GetString() == "Vector3"),
            "IDA imports Vector3 as a 12-byte structure"
        );
        Check(
            result.GetProperty("recursive_owner_size").GetUInt64() == 8 && result.GetProperty("recursive_union_size").GetUInt64() == 8,
            "IDA imports a union value member whose structure points back to the union"
        );
        Check(
            functions.Single(f => f.GetProperty("address").GetString() == "0x140001030").GetProperty("name").GetString() == "Incomplete",
            "IDA imports untyped native function symbols too"
        );
        Check(
            functions.Single(f => f.GetProperty("address").GetString() == "0x140001040").GetProperty("name").GetString() == "_ZN7Vector36LengthEf"
                && functions.Single(f => f.GetProperty("address").GetString() == "0x140001050").GetProperty("name").GetString()
                    == "_ZN7Vector35ResetEv",
            "IDA uses mangled DW_AT_linkage_name for both typed and untyped functions, not their plain source names"
        );
        var globals = result.GetProperty("globals").EnumerateArray().ToArray();
        Check(
            globals.Single(g => g.GetProperty("address").GetString() == "0x140002000").GetProperty("size").GetUInt64() == 12
                && globals.Single(g => g.GetProperty("address").GetString() == "0x140002000").GetProperty("is_struct").GetBoolean(),
            "IDA applies a global structure at its actual data address without a supplement script"
        );
        Check(
            globals.Single(g => g.GetProperty("address").GetString() == "0x140002020").GetProperty("is_ptr").GetBoolean()
                && globals.Single(g => g.GetProperty("address").GetString() == "0x140003000").GetProperty("is_ptr").GetBoolean(),
            "IDA applies pointer-cache types in initialized data and BSS without dereferencing them"
        );
        Check(
            globals.Single(g => g.GetProperty("address").GetString() == "0x140002040").GetProperty("is_array").GetBoolean()
                && globals.Single(g => g.GetProperty("address").GetString() == "0x140002040").GetProperty("size").GetUInt64() == 48,
            "IDA applies global array dimensions and the complete data extent"
        );
    }
}
