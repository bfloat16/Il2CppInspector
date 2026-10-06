# TOT Tests

Decode metadata and verify the standard analysis/output pipeline:

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --tot '<TOT Native directory>'
```

Verify native registration declarations and export real DWARF:

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --tot-native '<TOT Native directory>' '<output directory>'
readelf --file-header --sections '<output directory>/libil2cpp.sym'
```

The native regression verifies standard Unity registration signatures, runtime type references and typed DWARF export after TOT suppresses its decoding adapter.
