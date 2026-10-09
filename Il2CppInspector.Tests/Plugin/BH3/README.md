# BH3 Checks

Validated on 2026-10-09 with the existing inputs in
`H:/Project/Game/CN/崩坏3/9.1.0_Windows/Native`:
`UserAssembly.dll`, `global-metadata.dat` and `startup-metadata.dat`.

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --bh3 '<BH3 Native directory>'
```

Checks cover decoded table boundaries, signed constants, assembly identities,
type references, enum storage, generic methods, custom attributes, usages,
FieldRefs and generated runtime layouts. The fixture contains 77,799 real
types, 561,798 methods, 246,656 compiled generic bodies and 348,209 usages.

An optional output directory writes `runtime-check.cpp`; adding
`--write-header` also writes `il2cpp.h`:

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --bh3 '<BH3 Native directory>' '<output directory>' --write-header
```

These options generate files for further checks; the runner does not compile
the C++ assertions or perform a complete CLI export.
