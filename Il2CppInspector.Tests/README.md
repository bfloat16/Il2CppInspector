# Integration Tests

Tests mirror production modules under `Common/`, `CLI/` and `Plugin/<game>/`.
`Program.cs` selects a test mode and `TestAssert` supplies shared assertions.
Real input files are supplied locally; game-specific modes are documented with [ZZZ](Plugin/ZZZ/README.md) and [TOT](Plugin/TOT/README.md).

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --plugins
dotnet run --project Il2CppInspector.Tests -c Release -- --cpp-layout clang
dotnet run --project Il2CppInspector.Tests -c Release -- --stock '<Unity player directory>'
dotnet run --project Il2CppInspector.Tests -c Release -- --plugin-cli '<CLI DLL>' '<isolated directory>'
dotnet run --project Il2CppInspector.Tests -c Release -- --cli-targets '<CLI DLL>'
dotnet run --project Il2CppInspector.Tests -c Release -- --debug-routing
dotnet run --project Il2CppInspector.Tests -c Release -- --progress
dotnet run --project Il2CppInspector.Tests -c Release -- --pdb-fixture '<output directory>' llvm-pdbutil '<IDA idat executable>'
dotnet run --project Il2CppInspector.Tests -c Release -- --dwarf-fixture '<output directory>' readelf gdb '<IDA idat executable>' llvm-symbolizer
dotnet run --project Il2CppInspector.Tests -c Release -- --dwarf-globals '<Android sample directory>' '<output directory>' '<IDA idat executable>'
python Il2CppInspector.Tests/Common/Outputs/ScriptResourcesTests.py
```

## Native Fixtures

External validators are optional positional arguments; omit them to run serialization checks alone.
PDB fixtures cover LLVM record decoding and IDA function signatures, bitfields, enum widths and applied globals.
DWARF fixtures cover four architectures, ranged compilation units, references, debuglink CRCs and consumer address/type lookup.
IDA fixtures require clang, lld and llvm-objcopy on PATH and modify only temporary databases.
`--cpp-layout clang` independently verifies Unity header sizes on ARM, ARM64, x86 Linux and x64 Windows.
`--dwarf-globals` checks every known global in an Android directory containing `libil2cpp.so` and `global-metadata.dat`, without supplementary type application.
`--progress` also accepts a binary, metadata file and temporary DummyDll output directory for real progress checks.

## Adding Tests

Keep shared behavior under `Common/`, workflows under `CLI/` and game-specific checks under `Plugin/<game>/`.
Reuse expensive decoded models and cover independent failure modes rather than duplicating formatting assertions.
Document required local inputs beside the relevant tests.
