# ZZZ Tests

Generate DLLs and verify decoded data, reflection, assembly identities and output metadata:

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- '<ZZZ Native directory>' '<output directory>' --write-dll --dll-only
```

Verify existing ZZZ DLLs without loading the native inputs:

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --assembly-resolution '<ZZZ DummyDll directory>'
```

The resolution check uses Mono.Cecil 0.11.6, matching AnimeStudio.
It verifies assembly identities and cache resolution, including `UnityEngine.MonoBehaviour` through `UnityEngine.CoreModule`, without search directories.

Generate and verify a native PDB directly from the ZZZ analysis model, without generating DLLs, headers or JSON:

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- '<ZZZ Native directory>' '<output directory>' --pdb-only
llvm-pdbutil dump -summary -streams '<output directory>/GameAssembly.pdb'
```

The PDB check verifies typed native functions and runtime layouts while ensuring that constructed method objects remain lazy.

Generate and verify an ELF/DWARF debug companion directly from the same model:

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- '<ZZZ Native directory>' '<output directory>' --dwarf-only
readelf --file-header --sections '<output directory>/GameAssembly.sym'
```

The DWARF check verifies typed functions, lazy methods, layouts after field release and input stream position.

Verify minimal disassembler metadata and native coverage without writing debug files:

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- '<ZZZ Native directory>' '<output directory>' --debug-supplement
```

Compare header/JSON export time with and without PDB generation in the same model:

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- '<ZZZ Native directory>' '<baseline directory>' --native-profile
dotnet run --project Il2CppInspector.Tests -c Release -- '<ZZZ Native directory>' '<PDB directory>' --native-profile --with-pdb
```
