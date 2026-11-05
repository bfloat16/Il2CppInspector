# Integration Tests

`Program.cs` dispatches test modes across `Common/`, `CLI/`, `Plugin/` and `VersionedSerialization/`.

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --plugins
dotnet run --project Il2CppInspector.Tests -c Release -- --serialization
dotnet run --project Il2CppInspector.Tests -c Release -- --game <GameId> '<sample Native directory>' ['<output directory>']
dotnet run --project Il2CppInspector.Tests -c Release -- --cpp-layout [clang]
dotnet run --project Il2CppInspector.Tests -c Release -- --stock '<Unity player directory>'
dotnet run --project Il2CppInspector.Tests -c Release -- --plugin-cli '<CLI DLL>' '<isolated directory>'
dotnet run --project Il2CppInspector.Tests -c Release -- --cli-targets '<CLI DLL>'
dotnet run --project Il2CppInspector.Tests -c Release -- --progress
dotnet run --project Il2CppInspector.Tests -c Release -- --pdb-fixture '<output directory>' [llvm-pdbutil] ['<IDA idat executable>']
dotnet run --project Il2CppInspector.Tests -c Release -- --dwarf-fixture '<output directory>' [readelf] [gdb] ['<IDA idat executable>'] [llvm-symbolizer]
dotnet run --project Il2CppInspector.Tests -c Release -- --dwarf-globals '<Android sample directory>' '<output directory>' ['<IDA idat executable>']
python -I Il2CppInspector.Tests/Common/Outputs/ScriptResourcesTests.py
```

- `--plugins` verifies case-insensitive selection, truncated/foreign metadata rejection and `Samples` registration for every installed plugin.
- `--serialization` verifies versioned value/reference array construction and field boundaries across versions.
- `--game` checks metadata matching, concrete value-type header sizes, decoded table/layout baselines (`Plugin/GameSampleTests.cs`), and optional DummyDll reference resolution (stage NSH `common.dat` as `global-metadata.dat`).
- `--cpp-layout`, `--pdb-fixture`, `--dwarf-fixture` and `--dwarf-globals` run standalone checks by default and invoke external validators (`clang`, `lld`, `llvm-objcopy`, `llvm-pdbutil`, `readelf`, `gdb`, `llvm-symbolizer`, `idat`) when supplied.
- `ScriptResourcesTests.py` tests disassembler script templates, `function_boundaries_only` mode and `_t.py` terminal checkpoint/resume without requiring IDA or `tqdm`.
