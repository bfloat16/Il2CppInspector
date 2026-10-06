using System.Buffers.Binary;
using Il2CppInspector.Outputs.Dwarf;

namespace Il2CppInspector.Tests.Common.Outputs;

internal static class DwarfOutputTests
{
    private sealed record Die(ulong Offset, uint Tag, ulong Parent, Dictionary<uint, ulong> Attributes);

    private sealed class TestImage : FileFormatStream<TestImage>
    {
        public override string DefaultFilename => "native.bin";
        public override string Arch => Architecture;
        public string Architecture { get; set; }
        public override string Format => Bits == 64 ? "ELF64" : "ELF";
        public Section[] Sections { get; set; } = [];

        public override IEnumerable<Section> GetSections() => Sections;
    }

    internal static void Run(string directory, string readelf = null, string gdb = null, string idat = null, string symbolizer = null)
    {
        Directory.CreateDirectory(directory);
        using (var image = new TestImage { Bits = 64, Architecture = "x64" })
        {
            Check(DwarfOutput.Supports(image), "DWARF supports little-endian x64 inputs");
            image.Endianness = Bin2Object.Endianness.Big;
            Check(!DwarfOutput.Supports(image), "DWARF rejects big-endian input rather than claiming incorrect bitfield semantics");
            image.Endianness = Bin2Object.Endianness.Little;
            image.Architecture = "Unsupported";
            Check(!DwarfOutput.Supports(image), "DWARF rejects unsupported machine types");
            image.Architecture = "x86";
            Check(!DwarfOutput.Supports(image), "DWARF rejects inconsistent pointer sizes and machine types");
        }
        foreach (var (bits, arch, machine) in new[] { (64, "x64", 62), (32, "x86", 3), (32, "ARM", 40), (64, "ARM64", 183) })
        {
            var scalar = new CppType("float", 32);
            var vector = new CppComplexType(ComplexValueType.Struct) { Name = "Vector3" };
            foreach (var name in new[] { "x", "y", "z" })
                vector.AddField(name, scalar);
            var node = new CppComplexType(ComplexValueType.Struct) { Name = "Node" };
            node.AddField("next", node.AsPointer(bits));
            var union = new CppComplexType(ComplexValueType.Union) { Name = "ValueUnion" };
            union.AddField("scalar", scalar);
            union.AddField("vector", vector);
            var owner = new CppComplexType(ComplexValueType.Struct) { Name = "RecursiveOwner" };
            var recursiveUnion = new CppComplexType(ComplexValueType.Union) { Name = "RecursiveValue" };
            owner.AddField("owner", recursiveUnion.AsPointer(bits));
            recursiveUnion.AddField("value", owner);
            recursiveUnion.AddField("integer", new CppType("int", 32));
            var enumeration = new CppEnumType(new CppType("int", 32)) { Name = "SignedEnum" };
            enumeration.AddField("Negative", -1);
            var unsigned = new CppEnumType(new CppType("uint64_t", 64)) { Name = "UnsignedEnum" };
            unsigned.AddField("Maximum", ulong.MaxValue);
            var flags = new CppComplexType(ComplexValueType.Struct) { Name = "Flags" };
            flags.AddField("ready", new CppType("uint32_t", 32), bitfield: 1);
            flags.AddField("mode", new CppType("uint32_t", 32), bitfield: 3);
            var signature = new CppFnPtrType(bits, scalar, [("value", vector.AsPointer(bits)), ("array", vector.AsArray(4))]);
            var holder = new CppComplexType(ComplexValueType.Struct) { Name = "Holder" };
            holder.AddField(new CppField("constant", new CppType("int", 32), isConst: true));
            holder.AddField("constPointee", vector.AsConst().AsPointer(bits));
            holder.AddField("volatileValue", new CppVolatileType(new CppType("int", 32)));
            holder.AddField("callback", signature);
            var tail = new CppComplexType(ComplexValueType.Struct) { Name = "PaddedTail" };
            tail.AddField("pointer", vector.AsPointer(bits));
            tail.AddField("byte", new CppType("uint8_t", 8));
            var nested = new CppComplexType(ComplexValueType.Struct) { Name = "NestedTail" };
            nested.AddField("byte", new CppType("uint8_t", 8));
            nested.AddField("value", tail);
            nested.AddField("after", new CppType("uint32_t", 32));
            var nativeTypes = new List<CppType>
            {
                owner,
                recursiveUnion,
                vector,
                node,
                union,
                enumeration,
                unsigned,
                flags,
                holder,
                tail,
                nested,
                scalar.AsAlias("ScalarAlias"),
                vector.AsArray(4).AsArray(2),
                new CppForwardDefinitionType("Opaque"),
            };
            if (arch == "x64")
            {
                var large = new CppComplexType(ComplexValueType.Struct) { Name = "LargeFields" };
                for (var i = 0; i < 130000; i++)
                    large.Fields.Add(i * 32, [new CppField("member_" + i, scalar) { Offset = i * 32 }]);
                nativeTypes.Add(large);
            }
            var address = bits == 64 ? 0x140001000UL : 0x1000UL;
            var code = new Section
            {
                Name = ".text",
                VirtualStart = address,
                VirtualEnd = address + 0xFFF,
                IsExec = true,
            };
            var data = new Section
            {
                Name = ".data",
                VirtualStart = address + 0x1000,
                VirtualEnd = address + 0x10FF,
                IsData = true,
            };
            var bss = new Section
            {
                Name = ".bss",
                VirtualStart = address + 0x2000,
                VirtualEnd = address + 0x203F,
                IsBSS = true,
            };
            using var document = new DwarfDocument(
                bits,
                arch,
                "GameAssembly.dll",
                [code],
                [address, address + 0x20, address + 0x30, address + 0x40, address + 0x50],
                maxUnitBytes: arch == "ARM64" ? 128 : 256 * 1024,
                dataSections: [data, bss]
            );
            document.Method(new("Vector3_Length", address + 0x20 + (arch == "ARM" ? 1UL : 0UL), signature));
            document.Method(new("Incomplete", address + 0x30, new CppFnPtrType(bits, new CppType("Unknown", 32), []), false));
            document.Method(new("SharedAddress", address + 0x20, signature));
            document.Method(new("_ZN7Vector36LengthEf", address + 0x40, new CppFnPtrType(bits, scalar, [("value", scalar)])) { SourceName = "Length" });
            document.Method(new("_ZN7Vector35ResetEv", address + 0x50, null, false) { SourceName = "Reset" });
            document.Method(new("OutsideCode", address - 4, signature));
            document.Method(new("NullAddress", 0, signature));
            var path = Path.Combine(directory, "native-" + arch + ".sym");
            document.Write(
                path,
                nativeTypes,
                [
                    new("g_Vector3", data.VirtualStart, vector),
                    new("g_Vector3Pointer", data.VirtualStart + 0x20, vector.AsPointer(bits)),
                    new("g_Vector3Array", data.VirtualStart + 0x40, vector.AsArray(4)),
                    new("g_OddByte", data.VirtualStart + 0x81, new CppType("uint8_t", 8)),
                    new("g_BssPointer", bss.VirtualStart, vector.AsPointer(bits)),
                    new("g_Vector3", data.VirtualStart + 0x90, vector),
                    new("DuplicateAddress", data.VirtualStart, vector),
                    new("InCode", address + 0x100, vector),
                    new("OutsideData", data.VirtualStart - 4, vector),
                    new("CrossesDataEnd", data.VirtualEnd - 4, vector),
                    new("NullGlobal", 0, vector),
                    new("SentinelGlobal", ulong.MaxValue, vector),
                    new("UnknownGlobal", data.VirtualStart + 0xB0, null),
                    new("OpaqueGlobal", data.VirtualStart + 0xC0, new CppForwardDefinitionType("Opaque")),
                ]
            );
            Check(document.Functions == 5 && document.TypedFunctions == 3, $"{arch}: DWARF filters unmapped functions and omits incomplete prototypes");
            Check(document.Globals == 6, $"{arch}: DWARF filters invalid globals, code addresses, unknown storage and duplicate addresses");
            var file = File.ReadAllBytes(path);
            Check(file[4] == (bits == 64 ? 2 : 1) && U16(file, 18) == machine, $"{arch}: ELF class and machine match the input architecture");
            var sections = ReadSections(file);
            var dies = ReadDies(sections[".debug_info"], sections[".debug_abbrev"], bits);
            var byOffset = dies.ToDictionary(d => d.Offset);
            Check(dies.Where(d => d.Tag == 0x2E).All(d => !d.Attributes.ContainsKey(0x3F)), $"{arch}: synthesized function DIEs do not declare external linkage");
            VerifyFunctionBindings(file, sections[".symtab"], bits, document.Functions);
            var strings = sections[".debug_str"];
            string Name(Die die) => die.Attributes.TryGetValue(3, out var offset) ? Text(strings, offset) : "";
            var named = dies.Where(d => d.Attributes.ContainsKey(3)).GroupBy(Name).ToDictionary(g => g.Key, g => g.ToArray());
            Die Named(string name) => named[name][0];
            var globals = dies.Where(d => d.Tag == 0x34).ToArray();
            Check(
                globals.Length == 6 && globals.All(d => d.Attributes.ContainsKey(0x3F) && d.Attributes.ContainsKey(0x6E)),
                $"{arch}: global definitions have external linkage, names and type references"
            );
            Check(
                Named("g_Vector3").Attributes[2] == data.VirtualStart && byOffset[Named("g_Vector3").Attributes[0x49]].Tag == 0x13,
                $"{arch}: inline global structures carry their actual memory address"
            );
            Check(
                Named("g_Vector3Pointer").Attributes[2] == data.VirtualStart + 0x20 && byOffset[Named("g_Vector3Pointer").Attributes[0x49]].Tag == 0x0F,
                $"{arch}: pointer caches remain pointers rather than inline structures"
            );
            Check(
                byOffset[Named("g_Vector3Array").Attributes[0x49]].Tag == 0x01
                    && Named("g_BssPointer").Attributes[2] == bss.VirtualStart
                    && Named("g_OddByte").Attributes[2] == data.VirtualStart + 0x81,
                $"{arch}: arrays, BSS and odd data addresses retain their storage semantics"
            );
            Check(Named($"g_Vector3_{data.VirtualStart + 0x90:X}").Attributes[2] == data.VirtualStart + 0x90, $"{arch}: repeated variable names at distinct cache addresses stay unique");
            foreach (var die in dies.Where(d => d.Attributes.ContainsKey(0x49)))
                if (!byOffset.ContainsKey(die.Attributes[0x49]))
                    throw new InvalidDataException($"Unresolved DWARF reference at {die.Offset:X}.");
            Pass($"{arch}: every DWARF type reference targets a complete DIE boundary");
            ulong UnitOf(Die die)
            {
                while (die.Parent != 0)
                    die = byOffset[die.Parent];
                return die.Offset;
            }
            Check(dies.Where(d => d.Attributes.ContainsKey(0x49)).All(d => UnitOf(d) == UnitOf(byOffset[d.Attributes[0x49]])), $"{arch}: every type reference stays within its compilation unit");
            if (arch == "ARM64")
                Check(document.CompilationUnits > 1 && named["Vector3"].Length > 1, "The IDA fixture repeats complete structure definitions across compilation units");
            VerifyUnitRanges(sections, dies, bits);
            VerifyLineTables(sections, dies, bits);
            VerifyStringSection(file);
            var function = Named("Vector3_Length");
            Check(
                Text(strings, Named("_ZN7Vector36LengthEf").Attributes[0x6E]) == "_ZN7Vector36LengthEf"
                    && Text(strings, Named("_ZN7Vector35ResetEv").Attributes[0x6E]) == "_ZN7Vector35ResetEv"
                    && Text(strings, function.Attributes[0x6E]) == "Vector3_Length",
                $"{arch}: typed and untyped functions retain full native names instead of bare source names"
            );
            Check(
                function.Attributes[0x11] == address + 0x20 && function.Attributes[0x12] == 0x10 && !Named("Incomplete").Attributes.ContainsKey(0x49),
                $"{arch}: function address ranges and signature completeness are preserved"
            );
            var vectorDie = Named("Vector3");
            Check(
                Named("PaddedTail").Attributes[0x0B] == (ulong)(bits == 64 ? 16 : 8) && Named("NestedTail").Attributes[0x0B] == (ulong)(bits == 64 ? 32 : 16),
                $"{arch}: serialized structure sizes include tail padding and nested aggregate alignment"
            );
            var constPointer = byOffset[Named("constPointee").Attributes[0x49]];
            Check(
                constPointer.Tag == 0x0F && byOffset[constPointer.Attributes[0x49]].Tag == 0x26 && byOffset[Named("volatileValue").Attributes[0x49]].Tag == 0x35,
                $"{arch}: DWARF retains const pointees and volatile objects at the correct qualifier level"
            );
            var fields = dies.Where(d => d.Parent == vectorDie.Offset).ToArray();
            Check(
                vectorDie.Attributes[0x0B] == 12 && fields.Select(Name).SequenceEqual(["x", "y", "z"]) && fields.Select(d => d.Attributes[0x38]).SequenceEqual([0UL, 4UL, 8UL]),
                $"{arch}: vector field sizes and offsets match the C++ model"
            );
            Check(
                Named("SignedEnum_Negative").Attributes[0x1C] == ulong.MaxValue && Named("UnsignedEnum_Maximum").Attributes[0x1C] == ulong.MaxValue,
                $"{arch}: qualified enum names and signed/unsigned values survive LEB encoding"
            );
            Check(Named("ready").Attributes[0x0D] == 1 && Named("mode").Attributes[0x0D] == 3 && Named("mode").Attributes[0x6B] == 1, $"{arch}: bitfield sizes and absolute bit offsets are preserved");
            Check(Named("ValueUnion").Tag == 0x17 && dies.Where(d => d.Parent == Named("ValueUnion").Offset).All(d => d.Attributes[0x38] == 0), $"{arch}: unions retain overlapping fields");
            Check(Named("RecursiveOwner").Offset < Named("RecursiveValue").Offset, $"{arch}: recursive pointer cycles do not put a union's value member after the union");
            Check(dies.Any(d => d.Tag == 0x21 && d.Attributes[0x37] == 4) && dies.Any(d => d.Tag == 0x21 && d.Attributes[0x37] == 2), $"{arch}: nested arrays retain each dimension");
            Check(byOffset[Named("next").Attributes[0x49]].Attributes[0x49] == Named("Node").Offset, $"{arch}: recursive pointer types resolve without duplicate aggregate declarations");
            Check(
                Named("ScalarAlias").Tag == 0x16 && byOffset[Named("constant").Attributes[0x49]].Tag == 0x26 && dies.Any(d => d.Tag == 0x15),
                $"{arch}: typedefs, const fields and function pointers retain type information"
            );
            Check(
                dies.Where(d => d.Tag == 0x0F).All(d => d.Attributes[0x0B] == (ulong)bits / 8) && Named("Opaque").Attributes.ContainsKey(0x3C),
                $"{arch}: pointer sizes and opaque declarations are correct"
            );
            Check(
                ReadSymbolAddresses(sections[".symtab"], bits).Take(5).SequenceEqual([address + 0x20, address + 0x30, address + 0x20, address + 0x40, address + 0x50]),
                $"{arch}: ELF symbols preserve aliases sharing a code address"
            );
            VerifyGlobalSymbols(file, sections, globals, byOffset, bits);
            Check(
                Enumerable
                    .Range(1, 5)
                    .Select(i => Text(sections[".strtab"], U32(sections[".symtab"], i * (bits == 64 ? 24 : 16))))
                    .SequenceEqual(["Vector3_Length", "Incomplete", "SharedAddress", "_ZN7Vector36LengthEf", "_ZN7Vector35ResetEv"]),
                $"{arch}: ELF symbols use the same native linkage names as DWARF"
            );
            if (arch == "x64")
                Check(sections[".debug_info"].Length > 1024 * 1024 && Named("member_129999").Attributes[0x38] == 129999 * 4, "Large DWARF layouts resolve fixups across streaming buffer boundaries");
            if (readelf != null)
            {
                var text = Tool(readelf, ["--file-header", "--sections", "--symbols", "--debug-dump=info", "--debug-dump=rawline", "--debug-dump=aranges", "--debug-dump=Ranges", path]);
                Check(
                    text.Contains("Vector3_Length")
                        && text.Contains("DW_TAG_subprogram")
                        && text.Contains("DW_AT_data_bit_offset")
                        && text.Contains("DW_TAG_subroutine_type")
                        && text.Contains("DW_TAG_variable")
                        && text.Contains("DW_OP_addr"),
                    $"{arch}: GNU readelf reads ELF symbols and DWARF types without warnings"
                );
            }
            if (gdb != null && arch is "x64" or "x86")
            {
                var text = Tool(
                    gdb,
                    [
                        "-nx",
                        "-batch",
                        "-ex",
                        "symbol-file " + path.Replace('\\', '/'),
                        "-ex",
                        "ptype Vector3",
                        "-ex",
                        "ptype Flags",
                        "-ex",
                        "ptype Vector3_Length",
                        "-ex",
                        "info address Vector3_Length",
                        "-ex",
                        "info address Vector3::Length(float)",
                        "-ex",
                        "ptype g_Vector3",
                        "-ex",
                        "ptype g_Vector3Pointer",
                        "-ex",
                        "ptype g_Vector3Array",
                        "-ex",
                        "info address g_Vector3",
                    ]
                );
                Check(
                    text.Contains("float x;")
                        && text.Contains("mode : 3")
                        && text.Contains("type = float (struct Vector3 *, struct Vector3 [4])")
                        && text.Contains((address + 0x20).ToString("x"))
                        && text.Contains((address + 0x40).ToString("x")),
                    $"{arch}: GDB loads structures, bitfields, function signatures and addresses"
                );
                Check(
                    text.Contains("} *") && text.Contains("} [4]") && text.Contains(data.VirtualStart.ToString("x")),
                    $"{arch}: GDB resolves global structures, pointer caches, arrays and their addresses"
                );
            }
            if (idat != null && arch == "ARM64")
                DwarfIdaTests.Run(directory, path, idat);
        }
        RunRangeFixture(directory, symbolizer);
        RunNameFixture(directory);
        RunDebugLinkFixture(directory);
    }

    private static void RunNameFixture(string directory)
    {
        var complete = "Game_" + new string('N', 300) + "_Outer_Inner_M\u00E9thod_842_Generic_17";
        var overload = complete + "_1";
        var untyped = "_ZN5Other5Outer5Inner5ResetEv";
        var code = new Section
        {
            Name = ".text",
            VirtualStart = 0x1000,
            VirtualEnd = 0x10FF,
            IsExec = true,
        };
        var signature = new CppFnPtrType(64, new CppType("void"), [("value", new CppType("int", 32))]);
        using var document = new DwarfDocument(64, "ARM64", "names", [code], [0x1000, 0x1010, 0x1020, 0x1030], maxUnitBytes: 64);
        document.Method(new("Inner_Method", 0x1000, signature) { SourceName = "Method", LinkageName = complete });
        document.Method(new("Inner_Method_1", 0x1010, signature) { SourceName = "Method", LinkageName = overload });
        document.Method(new("Inner_Reset", 0x1020, null, false) { SourceName = "Reset", LinkageName = untyped });
        var path = Path.Combine(directory, "native-names.sym");
        document.Write(path, []);
        var sections = ReadSections(File.ReadAllBytes(path));
        var dies = ReadDies(sections[".debug_info"], sections[".debug_abbrev"], 64).Where(d => d.Tag == 0x2E).ToArray();
        var names = dies.Select(d => Text(sections[".debug_str"], d.Attributes[3])).ToArray();
        Check(
            document.CompilationUnits > 1 && names.SequenceEqual([complete, overload, untyped]),
            "Complete JSON symbol names, overload identities and long UTF-8 names survive CU boundaries unchanged"
        );
        Check(
            dies.All(d => Text(sections[".debug_str"], d.Attributes[3]) == Text(sections[".debug_str"], d.Attributes[0x6E])),
            "DW_AT_name and DW_AT_linkage_name carry the same complete function name"
        );
        var symbols = sections[".symtab"];
        Check(Enumerable.Range(1, 3).Select(i => Text(sections[".strtab"], U32(symbols, i * 24))).SequenceEqual(names), "ELF function symbols use the same complete names as their DWARF DIEs");
        Check(names.All(n => !n.Contains('(') && !n.Contains(')')) && dies.All(d => !d.Attributes.ContainsKey(0x3F)), "Complete function names do not reintroduce parameter lists or external linkage");
    }

    private static void RunRangeFixture(string directory, string symbolizer)
    {
        const int count = 30000;
        foreach (var (bits, arch) in new[] { (64, "x64"), (32, "ARM") })
        {
            const ulong start = 0x1000;
            var code = new Section
            {
                Name = ".text",
                VirtualStart = start,
                VirtualEnd = start + count * 16 - 1,
                IsExec = true,
            };
            // Odd slots remain holes; even slots visit addresses in metadata order.
            var addresses = Enumerable.Range(0, count / 2).Select(i => start + (ulong)i * 32).ToArray();
            var known = Enumerable.Range(0, count).Select(i => start + (ulong)i * 16);
            using var document = new DwarfDocument(bits, arch, "multi", [code], known);
            for (var i = 0; i < addresses.Length; i++)
            {
                var index = i * 7919 % addresses.Length;
                document.Method(new("f" + index, addresses[index], null, false));
            }
            var path = Path.Combine(directory, "multi-" + arch + ".sym");
            document.Write(path, []);
            var sections = ReadSections(File.ReadAllBytes(path));
            var dies = ReadDies(sections[".debug_info"], sections[".debug_abbrev"], bits);
            Check(document.CompilationUnits > 1, $"{arch}: shuffled functions span multiple compilation units");
            VerifyUnitRanges(sections, dies, bits);
            VerifyLineTables(sections, dies, bits);
            Pass($"{arch}: CU ranges and aranges contain exactly their functions, preserving address holes");
            if (symbolizer != null)
            {
                var dwarfOnly = Path.Combine(directory, "multi-" + arch + "-dwarf-only.sym");
                Tool("llvm-objcopy", ["--remove-section=.symtab", "--remove-section=.strtab", path, dwarfOnly]);
                var indices = new[] { 0, 1, 10000, 14999 }.Select(i => i * 7919 % addresses.Length).ToArray();
                var arguments = new[] { "--no-demangle", "--obj=" + dwarfOnly }.Concat(indices.Select(i => "0x" + addresses[i].ToString("x"))).Append("0x1010").ToArray();
                var lines = Tool(symbolizer, arguments).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                Check(
                    indices.Select((index, i) => lines[i * 2] == "f" + index).All(match => match) && lines[^2] == "??",
                    $"{arch}: LLVM resolves shuffled functions across CUs from DWARF alone and leaves holes unmapped"
                );
            }
        }
    }

    private static void RunDebugLinkFixture(string directory)
    {
        foreach (var (bits, arch, machine) in new[] { (64, "x64", 62), (32, "ARM", 40) })
        {
            var output = Path.Combine(directory, "debuglink-" + arch);
            Directory.CreateDirectory(output);
            var binary = new byte[128];
            new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F', (byte)(bits == 64 ? 2 : 1), 1 }.CopyTo(binary, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(18), (ushort)machine);
            var name = "libfixture.sym.so";
            Encoding.UTF8.GetBytes(name).CopyTo(binary, 64);
            var crcOffset = 64 + ((name.Length + 1 + 3) & ~3);
            BinaryPrimitives.WriteUInt32LittleEndian(binary.AsSpan(crcOffset), 0xdeadbeef);
            var section = new Section
            {
                Name = ".gnu_debuglink",
                ImageStart = 64,
                ImageEnd = (uint)(crcOffset + 3),
            };
            using var image = new TestImage
            {
                Bits = bits,
                Architecture = arch,
                Sections = [section],
            };
            image.Write(0, binary);
            image.Position = 7;
            var input = Path.Combine(output, "libfixture.so");
            var debug = Path.Combine(output, name);
            File.WriteAllBytes(input, binary);
            File.WriteAllBytes(debug, Encoding.ASCII.GetBytes("123456789"));
            Check(DwarfOutput.GetCompanionName(image, "fallback.sym") == name && image.Position == 7, $"{arch}: GNU debuglink name extraction preserves stream position");
            var copy = DwarfOutput.CreateDebugLinkImage(image, input, debug);
            var patched = File.ReadAllBytes(copy);
            Check(U32(patched, crcOffset) == 0xcbf43926, $"{arch}: debuglink CRC matches the standard GNU CRC-32 test vector");
            Check(
                File.ReadAllBytes(input).SequenceEqual(binary)
                    && image.Position == 7
                    && patched.AsSpan(0, crcOffset).SequenceEqual(binary.AsSpan(0, crcOffset))
                    && patched.AsSpan(crcOffset + 4).SequenceEqual(binary.AsSpan(crcOffset + 4)),
                $"{arch}: debuglink output changes only the copied ELF checksum, preserving the input"
            );
            image.Sections =
            [
                new Section
                {
                    Name = ".gnu_debuglink",
                    ImageStart = 64,
                    ImageEnd = (uint)(crcOffset - 1),
                },
            ];
            Check(DwarfOutput.GetCompanionName(image, "fallback.sym") == "fallback.sym", $"{arch}: truncated debuglink without a CRC is rejected");
        }
    }

    private static void VerifyStringSection(byte[] file)
    {
        var is64 = file[4] == 2;
        var table = checked((int)(is64 ? U64(file, 40) : U32(file, 32)));
        var entry = U16(file, is64 ? 58 : 46);
        var count = U16(file, is64 ? 60 : 48);
        var header = table + U16(file, is64 ? 62 : 50) * entry;
        var namesOffset = checked((int)(is64 ? U64(file, header + 24) : U32(file, header + 16)));
        for (var i = 1; i < count; i++)
        {
            header = table + i * entry;
            if (Text(file, (ulong)namesOffset + U32(file, header)) != ".debug_str")
                continue;
            var flags = is64 ? U64(file, header + 8) : U32(file, header + 8);
            var size = is64 ? U64(file, header + 56) : U32(file, header + 36);
            var alignment = is64 ? U64(file, header + 48) : U32(file, header + 32);
            Check(flags == 0x30 && size == 1 && alignment == 1, "Mergeable DWARF strings have byte-sized entries and alignment like LLVM");
            return;
        }
        throw new InvalidDataException("Missing .debug_str section.");
    }

    private static void VerifyFunctionBindings(byte[] file, byte[] symbols, int bits, int functions)
    {
        var table = checked((int)(bits == 64 ? U64(file, 40) : U32(file, 32)));
        var headerSize = bits == 64 ? 64 : 40;
        var symbolSize = bits == 64 ? 24 : 16;
        var symbolInfo = bits == 64 ? 4 : 12;
        var firstGlobal = 0u;
        for (var i = 1; i < U16(file, bits == 64 ? 60 : 48); i++)
            if (U32(file, table + i * headerSize + 4) == 2)
                firstGlobal = U32(file, table + i * headerSize + (bits == 64 ? 44 : 28));
        Check(firstGlobal == functions + 1, "ELF sh_info includes the undefined symbol and every local function");
        var functionCount = 0;
        for (var i = 0; i < symbols.Length / symbolSize; i++)
        {
            var info = symbols[i * symbolSize + symbolInfo];
            Check((info >> 4 == 0) == (i < firstGlobal), "ELF local symbols precede the global symbol range");
            if ((info & 0xF) == 2)
            {
                Check(info >> 4 == 0, "Generated ELF function names use STB_LOCAL rather than STB_GLOBAL");
                functionCount++;
            }
        }
        Check(functionCount == functions, "Changing linkage retains every ELF function symbol");
    }

    private static List<(ulong Low, ulong High)> ReadRanges(byte[] data, ulong offset, int bits)
    {
        using var reader = new BinaryReader(new MemoryStream(data));
        reader.BaseStream.Position = checked((long)offset);
        var result = new List<(ulong, ulong)>();
        while (true)
        {
            var low = ReadForm(reader, 1, bits);
            var high = ReadForm(reader, 1, bits);
            if (low == 0 && high == 0)
                return result;
            if (low >= high || (result.Count > 0 && low <= result[^1].Item2))
                throw new InvalidDataException("CU ranges must be sorted, nonempty and coalesced.");
            result.Add((low, high));
        }
    }

    private static void VerifyUnitRanges(Dictionary<string, byte[]> sections, List<Die> dies, int bits)
    {
        var units = dies.Where(d => d.Tag == 0x11).ToDictionary(d => d.Offset);
        var ranges = new Dictionary<ulong, List<(ulong Low, ulong High)>>();
        foreach (var unit in units.Values)
        {
            Check(unit.Attributes[0x11] == 0 && !unit.Attributes.ContainsKey(0x12), "Range-list CUs have a zero base and no conflicting high_pc");
            var actual = ReadRanges(sections[".debug_ranges"], unit.Attributes[0x55], bits);
            var expected = new List<(ulong Low, ulong High)>();
            foreach (var function in dies.Where(d => d.Tag == 0x2e && d.Parent == unit.Offset).OrderBy(d => d.Attributes[0x11]))
            {
                var low = function.Attributes[0x11];
                var high = low + function.Attributes[0x12];
                if (expected.Count > 0 && low <= expected[^1].High)
                    expected[^1] = (expected[^1].Low, Math.Max(expected[^1].High, high));
                else
                    expected.Add((low, high));
            }
            Check(actual.SequenceEqual(expected), "CU address ranges match its children without including other units or holes");
            var addressRanges = expected
                .Concat(dies.Where(d => d.Tag == 0x34 && d.Parent == unit.Offset).Select(d => (Low: d.Attributes[2], High: d.Attributes[2] + TypeSize(d.Attributes[0x49]))))
                .OrderBy(r => r.Low);
            var merged = new List<(ulong Low, ulong High)>();
            foreach (var span in addressRanges)
                if (merged.Count > 0 && span.Low <= merged[^1].High)
                    merged[^1] = (merged[^1].Low, Math.Max(merged[^1].High, span.High));
                else
                    merged.Add(span);
            ranges.Add(unit.Offset - 11, merged);
        }
        using var reader = new BinaryReader(new MemoryStream(sections[".debug_aranges"]));
        var seen = new HashSet<ulong>();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var start = reader.BaseStream.Position;
            var length = reader.ReadUInt32();
            Check(reader.ReadUInt16() == 2, "Aranges use DWARF version 2");
            ulong unit = reader.ReadUInt32();
            Check(reader.ReadByte() == bits / 8 && reader.ReadByte() == 0, "Aranges match the image address size");
            while ((reader.BaseStream.Position - start) % (bits / 8 * 2) != 0)
                reader.ReadByte();
            var actual = new List<(ulong, ulong)>();
            while (true)
            {
                var low = ReadForm(reader, 1, bits);
                var size = ReadForm(reader, 1, bits);
                if (low == 0 && size == 0)
                    break;
                actual.Add((low, low + size));
            }
            Check(
                reader.BaseStream.Position == start + 4 + length && seen.Add(unit) && actual.SequenceEqual(ranges[unit]),
                "Aranges tuples and CU references cover exactly the CU's code and global storage"
            );
        }
        Check(seen.Count == ranges.Count(r => r.Value.Count != 0), "Every CU containing code or globals has an aranges table");

        ulong TypeSize(ulong offset)
        {
            var type = dies.Single(d => d.Offset == offset);
            return type.Attributes.TryGetValue(0x0B, out var size) ? size : TypeSize(type.Attributes[0x49]);
        }
    }

    private static void VerifyGlobalSymbols(byte[] file, Dictionary<string, byte[]> sections, Die[] globals, Dictionary<ulong, Die> byOffset, int bits)
    {
        var table = checked((int)(bits == 64 ? U64(file, 40) : U32(file, 32)));
        var headerSize = bits == 64 ? 64 : 40;
        var symbols = sections[".symtab"];
        var symbolSize = bits == 64 ? 24 : 16;
        var objects = Enumerable.Range(1, symbols.Length / symbolSize - 1).Where(i => symbols[i * symbolSize + (bits == 64 ? 4 : 12)] == 0x11).ToArray();
        Check(objects.Length == globals.Length, "Every DWARF global has a matching ELF STT_OBJECT symbol");
        foreach (var (symbol, global) in objects.Zip(globals))
        {
            int offset = symbol * symbolSize;
            int section = U16(symbols, offset + (bits == 64 ? 6 : 14));
            int header = table + section * headerSize;
            ulong address = bits == 64 ? U64(symbols, offset + 8) : U32(symbols, offset + 4);
            ulong size = bits == 64 ? U64(symbols, offset + 16) : U32(symbols, offset + 8);
            ulong sectionStart = bits == 64 ? U64(file, header + 16) : U32(file, header + 12);
            ulong sectionSize = bits == 64 ? U64(file, header + 32) : U32(file, header + 20);
            Check(
                address == global.Attributes[2] && size == byOffset[global.Attributes[0x49]].Attributes[0x0B] && address >= sectionStart && address + size <= sectionStart + sectionSize,
                "Global ELF symbol addresses, sizes and section indices match their DWARF definitions"
            );
        }
    }

    private static void VerifyLineTables(Dictionary<string, byte[]> sections, List<Die> dies, int bits)
    {
        using var reader = new BinaryReader(new MemoryStream(sections[".debug_line"]));
        foreach (var unit in dies.Where(d => d.Tag == 0x11))
        {
            var start = checked((long)unit.Attributes[0x10]);
            reader.BaseStream.Position = start;
            var end = start + 4 + reader.ReadUInt32();
            Check(reader.ReadUInt16() == 4, "Line tables use DWARF version 4");
            var headerLength = reader.ReadUInt32();
            reader.BaseStream.Position += headerLength;
            Check(reader.ReadByte() == 0 && Uleb(reader) == (ulong)(bits / 8 + 1) && reader.ReadByte() == 2, "DW_LNE_set_address payload length matches the image pointer size");
            var address = ReadForm(reader, 1, bits);
            var ranges = ReadRanges(sections[".debug_ranges"], unit.Attributes[0x55], bits);
            Check(address == (ranges.Count == 0 ? 0 : ranges[0].Low), "Line table address matches the first CU range");
            Check(
                reader.ReadByte() == 0 && Uleb(reader) == 1 && reader.ReadByte() == 1 && reader.BaseStream.Position == end,
                "Line table extended operations and end_sequence close at the exact unit boundary"
            );
        }
    }

    private static string Tool(string executable, string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var errors = stderr.GetAwaiter().GetResult();
        Check(process.ExitCode == 0 && string.IsNullOrWhiteSpace(errors), $"{Path.GetFileName(executable)} accepts DWARF output: {errors}");
        return stdout.GetAwaiter().GetResult();
    }

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static ulong U64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));

    private static string Text(byte[] bytes, ulong offset)
    {
        var start = checked((int)offset);
        return Encoding.UTF8.GetString(bytes, start, Array.IndexOf(bytes, (byte)0, start) - start);
    }

    private static Dictionary<string, byte[]> ReadSections(byte[] file)
    {
        var is64 = file[4] == 2;
        var table = checked((int)(is64 ? U64(file, 40) : U32(file, 32)));
        var entrySize = U16(file, is64 ? 58 : 46);
        var count = U16(file, is64 ? 60 : 48);
        var names = SectionBytes(U16(file, is64 ? 62 : 50));
        var sections = new Dictionary<string, byte[]>();
        for (var i = 1; i < count; i++)
            if (U32(file, table + i * entrySize + 4) != 8)
                sections.Add(Text(names, U32(file, table + i * entrySize)), SectionBytes(i));
        return sections;

        byte[] SectionBytes(int index)
        {
            var header = table + index * entrySize;
            var offset = checked((int)(is64 ? U64(file, header + 24) : U32(file, header + 16)));
            var size = checked((int)(is64 ? U64(file, header + 32) : U32(file, header + 20)));
            return file.AsSpan(offset, size).ToArray();
        }
    }

    private static ulong[] ReadSymbolAddresses(byte[] symbols, int bits) =>
        Enumerable.Range(1, symbols.Length / (bits == 64 ? 24 : 16) - 1).Select(i => bits == 64 ? U64(symbols, i * 24 + 8) : U32(symbols, i * 16 + 4)).ToArray();

    private static List<Die> ReadDies(byte[] info, byte[] abbrev, int bits)
    {
        var definitions = ReadAbbreviations(abbrev);
        Check(definitions.Values.SelectMany(d => d.Attributes).Where(a => a.Attribute == 0x49).All(a => a.Form == 0x13), "Every DW_AT_type uses the CU-relative DW_FORM_ref4");
        using var reader = new BinaryReader(new MemoryStream(info));
        var result = new List<Die>();
        var units = 0;
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            units++;
            var cuStart = (ulong)reader.BaseStream.Position;
            var length = reader.ReadUInt32();
            var version = reader.ReadUInt16();
            var abbrevOffset = reader.ReadUInt32();
            var addressSize = reader.ReadByte();
            Check(length != uint.MaxValue && version == 4 && abbrevOffset == 0 && addressSize == bits / 8, "DWARF uses 32-bit units with version 4 and the image address size");
            var cuEnd = checked((long)cuStart + 4 + length);
            var parents = new Stack<ulong>();
            while (reader.BaseStream.Position < cuEnd)
            {
                var offset = (ulong)reader.BaseStream.Position;
                var code = Uleb(reader);
                if (code == 0)
                {
                    parents.Pop();
                    continue;
                }
                var definition = definitions[code];
                var attributes = new Dictionary<uint, ulong>();
                foreach (var (attribute, form) in definition.Attributes)
                {
                    var value = ReadForm(reader, form, bits);
                    attributes.Add(attribute, form == 0x13 ? cuStart + value : value);
                }
                result.Add(new(offset, definition.Tag, parents.TryPeek(out var parent) ? parent : 0, attributes));
                if (definition.Children)
                    parents.Push(offset);
            }
            Check(reader.BaseStream.Position == cuEnd && parents.Count == 0, $"DWARF unit {units} child lists close exactly at the unit boundary");
        }
        Check(units > 0, "DWARF contains at least one compilation unit");
        return result;
    }

    private static Dictionary<ulong, (uint Tag, bool Children, List<(uint Attribute, uint Form)> Attributes)> ReadAbbreviations(byte[] abbrev)
    {
        using var declarations = new BinaryReader(new MemoryStream(abbrev));
        var definitions = new Dictionary<ulong, (uint Tag, bool Children, List<(uint Attribute, uint Form)> Attributes)>();
        while (true)
        {
            var code = Uleb(declarations);
            if (code == 0)
                break;
            var tag = checked((uint)Uleb(declarations));
            var children = declarations.ReadByte() != 0;
            var attributes = new List<(uint, uint)>();
            while (true)
            {
                var attribute = checked((uint)Uleb(declarations));
                var form = checked((uint)Uleb(declarations));
                if (attribute == 0 && form == 0)
                    break;
                attributes.Add((attribute, form));
            }
            definitions.Add(code, (tag, children, attributes));
        }
        return definitions;
    }

    private static ulong ReadForm(BinaryReader reader, uint form, int bits) =>
        form switch
        {
            0x01 => bits == 64 ? reader.ReadUInt64() : reader.ReadUInt32(),
            0x05 => reader.ReadUInt16(),
            0x07 => reader.ReadUInt64(),
            0x0B => reader.ReadByte(),
            0x0D => Sleb(reader),
            0x0E or 0x10 or 0x13 or 0x17 => reader.ReadUInt32(),
            0x0F => Uleb(reader),
            0x18 => ReadLocation(reader, bits),
            0x19 => 1,
            _ => throw new InvalidDataException($"Unsupported DWARF form {form:X}."),
        };

    private static ulong ReadLocation(BinaryReader reader, int bits)
    {
        var length = Uleb(reader);
        Check(length == (ulong)(bits / 8 + 1) && reader.ReadByte() == 0x03, "Global exprloc contains exactly DW_OP_addr plus one pointer-sized address");
        return ReadForm(reader, 1, bits);
    }

    private static ulong Uleb(BinaryReader reader)
    {
        ulong value = 0;
        var shift = 0;
        byte next;
        do
        {
            next = reader.ReadByte();
            value |= (ulong)(next & 127) << shift;
            shift += 7;
        } while ((next & 128) != 0);
        return value;
    }

    private static ulong Sleb(BinaryReader reader)
    {
        ulong value = 0;
        var shift = 0;
        byte next;
        do
        {
            next = reader.ReadByte();
            value |= (ulong)(next & 127) << shift;
            shift += 7;
        } while ((next & 128) != 0);
        return shift < 64 && (next & 64) != 0 ? value | (ulong.MaxValue << shift) : value;
    }
}
