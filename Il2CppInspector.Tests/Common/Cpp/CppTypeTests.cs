namespace Il2CppInspector.Tests.Common.Cpp
{
    internal static class CppTypeTests
    {
        internal static void Run(string clang = null)
        {
            var unionTypes = new CppTypeCollection(64);
            var unionA = unionTypes.Struct("A");
            unionA.AddField("a", unionTypes.GetType("int32_t"));
            var unionB = unionTypes.Struct("B");
            unionB.AddField("b", unionTypes.GetType("int32_t"));
            var union = unionTypes.Union("U");
            union.AddField("first", unionA);
            union.AddField("second", unionB);
            Check(union.Flattened["a"].OffsetBytes == 0 && union.Flattened["b"].OffsetBytes == 0, "Overlapping nested C++ layouts retain both fields when flattened");
            var tail = unionTypes.Struct("Tail");
            tail.AddField("pointer", unionTypes.GetType("void *"));
            tail.AddField("byte", unionTypes.GetType("uint8_t"));
            Check(tail.SizeBytes == 16 && tail.Alignment == 8, "C++ structures include ABI tail padding");
            var inner = unionTypes.Struct("Inner");
            inner.AddField("byte", unionTypes.GetType("uint8_t"));
            inner.AddField("pointer", unionTypes.GetType("void *"));
            var outer = unionTypes.Struct("Outer");
            outer.AddField("byte", unionTypes.GetType("uint8_t"));
            outer.AddField("inner", inner);
            outer.AddField("tail", tail.AsArray(2));
            Check(outer["inner"].OffsetBytes == 8 && outer["tail"].OffsetBytes == 24 && outer.SizeBytes == 56, "Nested structures and arrays use complete sizes and maximum member alignment");
            var bits = unionTypes.Struct("Bits");
            bits.AddField("bit", unionTypes.GetType("uint32_t"), bitfield: 1);
            Check(bits.SizeBytes == 4 && bits.Alignment == 4, "Bitfield containers preserve their storage-unit alignment");
            unionTypes.AddFromDeclarationText("struct Qualified\n{\nconst int32_t* pointee;\nint32_t* const pointer;\n};");
            var qualified = unionTypes.GetComplexType("Qualified");
            Check(
                qualified["pointee"].Type is CppPointerType { ElementType: CppConstType } && qualified["pointer"].Type is CppConstType { ElementType: CppPointerType },
                "C++ parsing distinguishes const pointees from const pointers"
            );
            Check(unionTypes.NewDefaultEnum("NativeEnum").SizeBytes == 4, "Default native enums use int even on 64-bit targets");
            var unsigned = unionTypes.NewDefaultEnum("UnsignedNativeEnum");
            unsigned.AddField("AllBits", uint.MaxValue);
            Check(unsigned.UnderlyingType.Name == "uint32_t", "Default enums choose an unsigned int when their values require it");
            Check(CppFnPtrType.FromSignature(unionTypes, "void (*callback)(void)").Arguments.Count == 0, "A void parameter list is empty, not a void-valued argument");
            unionTypes.AddFromDeclarationText("struct Qualifiers\n{\nunsigned int flags : 3;\nvolatile int value;\n};");
            var qualifiers = unionTypes.GetComplexType("Qualifiers");
            Check(qualifiers["flags"].Type.Name == "unsigned int" && qualifiers["value"].Type is CppVolatileType, "C++ parsing retains unsigned bitfield storage and volatile members");
            var bitLayout = unionTypes.Struct("BitLayout");
            bitLayout.AddField("mantissa", unionTypes.GetType("uint32_t"), bitfield: 23);
            bitLayout.AddField("exponent", unionTypes.GetType("uint32_t"), bitfield: 8);
            bitLayout.AddField("sign", unionTypes.GetType("uint32_t"), bitfield: 1);
            Check(bitLayout["exponent"].Offset == 23 && bitLayout["sign"].Offset == 31 && bitLayout.SizeBytes == 4, "Adjacent bitfields pack inside one storage unit without per-member alignment");
            bitLayout.AddField("next", unionTypes.GetType("uint32_t"), bitfield: 20);
            bitLayout.AddField("another", unionTypes.GetType("uint32_t"), bitfield: 20);
            Check(bitLayout["another"].Offset == 64, "Bitfields do not straddle an unpacked storage unit");
            if (clang != null)
                VerifyCompilerLayouts(clang);
        }

        private static void VerifyCompilerLayouts(string clang)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "Il2CppInspector.slnx")))
                root = root.Parent;
            if (root == null)
                throw new DirectoryNotFoundException("Cannot locate compiler layout fixture");
            var fixture = Path.Combine(root.FullName, "Il2CppInspector.Tests", "Common", "Outputs", "Fixtures", "CppLayoutFixture.cpp");
            foreach (var target in new[] { "aarch64-linux-gnu", "armv7-linux-gnueabihf", "i686-linux-gnu", "x86_64-pc-windows-msvc" })
            {
                var start = new System.Diagnostics.ProcessStartInfo(clang)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (
                    var argument in new[]
                    {
                        "--target=" + target,
                        "-std=c++17",
                        "-ffreestanding",
                        "-fms-extensions",
                        "-fsyntax-only",
                        "-I" + Path.Combine(root.FullName, "Il2CppInspector.Common"),
                        fixture,
                    }
                )
                    start.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(start);
                var errors = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();
                Check(process.ExitCode == 0, $"clang verifies actual Unity header and nested/tail layouts for {target}: {errors.GetAwaiter().GetResult()}");
            }
        }
    }
}
