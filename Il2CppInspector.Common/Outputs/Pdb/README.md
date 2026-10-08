# Native PDB

`PdbOutput` writes native PDB files directly from `AppModel` for x64 PE inputs, without exported headers, JSON or Dummy DLLs.
The CLI selects PDB for x64 PE and [DWARF](../Dwarf/README.md) for ELF/Mach-O.

```bash
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll -i GameAssembly.dll -m global-metadata.dat -o output -t IDA -t Debug
```

## Records and Loading

The writer emits MSF 7.00, CodeView types, module procedures, symbol indices and PE section headers, preserving the PE's RSDS GUID and age.
Types retain ABI alignment, tail padding, arrays, unions, bitfields and const/volatile modifiers, matching the script header's C-compatible field view.
Enum constants use type-qualified names so IDA keeps enums with different storage types separate.
Functions include available invoker, attribute-generator, API and registration signatures; incomplete signatures remain untyped.
Known globals use typed `S_GDATA32` records, applying registration structures and pointer caches at their data addresses without a script.
Load the generated PDB explicitly if the PE points to another PDB, or has no RSDS record.
Disassembler targets combined with Debug omit header generation and import, with [debug symbol arrays emptied in JSON](../Dwarf/README.md#scripts-and-limits).
IDA may revise inferred prototypes or omit explicit `void()` types on jump wrappers; no user-definite function types or local locations are forced.

## LLVM References

- `CodeViewDebug::lowerRecordFieldList`: bit offsets within storage units and byte offsets of those units.
- `CodeViewDebug::lowerTypeEnum`: enum underlying types, field lists and values.
- `CodeViewDebug::emitDebugInfoForGlobal` and `SymbolRecordMapping::visitKnownRecord(DataSym)`: global type, section-relative offset, section index and name.

Serialization was adapted from `il2cpp_pdbgen`; see [NOTICE.md](NOTICE.md).
