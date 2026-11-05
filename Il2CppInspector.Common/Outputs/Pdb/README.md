# Native PDB

`PdbOutput` writes native MSF 7.00 / CodeView PDB files directly from `AppModel` for x64 PE inputs (and selects [DWARF](../Dwarf/README.md) for ELF/Mach-O).

```bash
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll -i GameAssembly.dll -m global-metadata.dat -o output -t IDA -t Debug
```

## Records and Loading

- Emits CodeView types, module procedures (`S_GPROC32`), global data symbols (`S_GDATA32`), public symbols and PE section headers while preserving the PE's RSDS GUID and age.
- Types preserve ABI alignment, tail padding, arrays, unions, bitfields and type-qualified enum constants; functions include available invoker, attribute-generator, API and registration signatures.
- When combined with disassembler targets, header generation is omitted and [debug symbol arrays are emptied in JSON](../Dwarf/README.md#scripts-and-limits).

Serialization was adapted from `il2cpp_pdbgen`; see [NOTICE.md](NOTICE.md).
