# Native DWARF

`DwarfOutput` writes DWARF 4 (DWARF32) directly from `AppModel` into a copy of little-endian x86, x64, ARM or ARM64 ELF/Mach-O images (and selects [PDB](../Pdb/README.md) for x64 PE).

```bash
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll -i libil2cpp.so -m global-metadata.dat -o output -t Debug -t IDA
```

## Loading and Records

- Load `<output>/<input-name>` directly; original code/data file offsets and virtual addresses are preserved. Fat Mach-O inputs emit one thin binary per architecture.
- Compilation units emit code ranges, line-table headers, `.debug_aranges` and local `DW_FORM_ref4` type trees. Types preserve ABI layout, alignment, tail padding, bitfields and enum type-qualified member names.
- Functions emit `DW_AT_name`, `DW_AT_linkage_name` and local ELF symbols with available signatures; globals emit `DW_TAG_variable` (`DW_OP_addr`) and ELF object symbols.

## Scripts and Limits

When combined with disassembler targets (`IDA`, `Ghidra`, `BinaryNinja`), `Debug` omits `il2cpp.h` generation and empties function/API symbol arrays in `il2cpp.json` so the script applies only strings, comments, runtime caches and cross-references on top of the imported debug symbols.
