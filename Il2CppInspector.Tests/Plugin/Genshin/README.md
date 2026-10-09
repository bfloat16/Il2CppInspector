# Genshin Checks

Inputs: `H:/Project/Game/CN/原神/7.1.0_Windows/Native` containing
`YuanShen.exe`, `global-metadata.dat` and `startup-metadata.dat`.

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --genshin '<Native>'
dotnet run --project Il2CppInspector.Tests -c Release -- --genshin '<Native>' '<output>' --write-header --write-json --write-dll
```

The runner loads the real model, then checks key boundaries and representative
inheritance, enum storage, List<T> accessors, AppDomain events, global method and
Invoker lookup, Math.PI, multi-block literals and runtime record offsets.
It also exercises just the native-string and empty-array JSON sections.
It does not iterate every member or run the complete repository test suite.

The optional output switches exercise the standard C++ header, JSON and DLL
writers. Output remains outside the source tree when using the analysis
directory. `runtime-check.cpp` contains physical runtime layout assertions and
can be checked with `clang++ -std=c++17 -fsyntax-only runtime-check.cpp`.
