# Native PDB Output

`PdbOutput` writes Windows native PDB files directly from `AppModel`. It does not
read exported C++ headers, JSON metadata or managed Dummy DLLs, and does not run
the Rust generator or external tools. LLVM is used only for validation.

The application model exposes native method signatures and C++ type objects.
Game analysis adapters supply those objects from decoded metadata and runtime
layouts. The ZZZ adapter resolves generic arguments without materializing its
entire constructed-method dictionary. Field offsets, overlapping storage,
arrays, enum values and bitfields come from the existing C++ model.

The writer emits MSF 7.00, TPI/IPI, DBI, module procedure records, public/global
symbol indices, type hashes, named streams and PE section headers. It copies
the PE's CodeView RSDS GUID and age, including the DBI age. Incomplete native
signatures retain their function names and addresses without claiming a typed
prototype. PDB output supports x64 PE binaries. A PE without RSDS produces an
empty-GUID PDB that must be loaded manually.

```csharp
var app = new AppModel(typeModel).Build(unityVersion);
var result = new PdbOutput(app).Write("GameAssembly.pdb");
```

Select output targets independently, using repeated `-t` options:

```bash
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll -i GameAssembly.dll -m global-metadata.dat -o output -t IDA -t PDB
```

Script exports run before PDB to keep their streaming memory profile. PDB still
consumes the in-memory model directly. `-t PDB` does not
generate headers or JSON. Script targets share one header and one metadata file;
a single script target writes `il2cpp.py`, while multiple script targets write
`il2cpp-IDA.py`, `il2cpp-Ghidra.py`, etc. `--target` and `--script-target` are
aliases for `-t`. Targets are case-insensitive and duplicates are ignored.

The native PDB serialization was adapted from `il2cpp_pdbgen`; see [NOTICE.md](NOTICE.md).
