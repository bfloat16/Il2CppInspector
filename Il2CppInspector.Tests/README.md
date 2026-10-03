# Integration Tests

The test directories mirror the production projects and their module folders:

```text
Il2CppInspector.Tests/
  CLI/                         CLI plugin discovery and deployment
  Common/
    Cpp/                       Shared C++ layout helpers
    IL2CPP/                    Stock loading
    Model/                     Stock application model
    Plugins/                   Shared plugin selection
    Outputs/                   Native PDB records and symbol indices
    Reflection/                Stock reflection
  Plugin/
    <game>/
      IL2CPP/                  Binary decoding and runtime-header checks
      Model/                   Public analysis model and address map
      Outputs/                 DLL, JSON, native header and C++ project checks
      Reflection/              Generic identities and shared field layouts
```

`Program.cs` dispatches the command-line test modes. `TestAssert` provides shared assertions, while each game test runner manages its input package and reflection model. Real game inputs are supplied locally and are not included in the repository.

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --plugins
dotnet run --project Il2CppInspector.Tests -c Release -- --stock '<Unity player directory>'
dotnet run --project Il2CppInspector.Tests -c Release -- --plugin-cli '<CLI DLL>' '<isolated directory>'
dotnet run --project Il2CppInspector.Tests -c Release -- --cli-targets '<CLI DLL>'
dotnet run --project Il2CppInspector.Tests -c Release -- --pdb-fixture '<output directory>' llvm-pdbutil
python Il2CppInspector.Tests/Common/Outputs/ScriptResourcesTests.py
```

Game-specific input arguments, modes and output options belong to the corresponding test runner. Keep their documentation alongside `Plugin/<game>/` rather than adding game-specific instructions to this README.

Coverage focuses on decoded data, generic/layout behavior, serialization and plugin deployment boundaries. Simple string formatting and file-existence checks are omitted; actual consumers verify the files they read.

## Adding Tests

1. Place shared behavior tests under `Common/` and CLI workflow tests under `CLI/`, following the production module structure.
2. Place game-specific tests under `Plugin/<game>/`, mirroring the corresponding plugin folders.
3. Reuse loaded packages and models within a test run when input decoding is expensive.
4. Cover regressions and behavior that can fail independently, and document required local inputs or existing exports with the relevant tests.
