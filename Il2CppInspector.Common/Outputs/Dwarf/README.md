# Native DWARF

`DwarfOutput` writes DWARF 4/DWARF32 directly from `AppModel` and merges it into a copy of the input ELF or Mach-O image.
It supports little-endian x86, x64, ARM and ARM64 images without reading exported headers, JSON or Dummy DLLs.
The CLI selects DWARF for ELF/Mach-O and [PDB](../Pdb/README.md) for x64 PE.

```bash
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll -i libil2cpp.so -m global-metadata.dat -o output -t Debug -t IDA
```

## Loading

Load `<output>/<input-name>` directly; it contains both the original executable code and the generated debug sections.
The original input is not modified, and loaded code/data retain their file offsets and virtual addresses.
Mach-O output requires sufficient load-command padding and removes invalidated code signatures.
Fat Mach-O inputs produce a thin output for each analyzed image in its respective output directory.
The standalone ELF companion and debuglink APIs remain available to library callers.
For ASLR, supply the load-address delta through the debugger's relocation setting.

## Records

Compilation units carry code ranges, line-table references and aranges, with types referenced within the same unit using `DW_FORM_ref4`.
Type dependencies are repeated when needed by another unit; connected type graphs are not split at the size threshold.
Value-type dependencies precede their users; pointer references can point forward for recursive types.
Functions preserve the same complete name used by the IDA JSON export in `DW_AT_name`, `DW_AT_linkage_name` and ELF function symbols, independently of their signatures.
Available signatures include invokers, attribute generators, IL2CPP APIs and registration functions.
Generated functions omit `DW_AT_external` and use local ELF symbols; existing runtime exports are preserved.
Function extents are bounded estimates from known code boundaries, and incomplete signatures remain untyped.
Types preserve ABI alignment, tail padding, bitfields and const/volatile qualification, using the script header's C-compatible field view.
Enum members use type-qualified names to prevent IDA from merging different enums.
Known globals use `DW_TAG_variable` with `DW_OP_addr` storage locations and corresponding ELF object symbols, including registration structures and metadata pointer caches.
Unmapped, unknown-sized and duplicate data addresses are excluded.
Large outputs stream through disposable temporary files rather than retaining all DWARF bytes in memory.

## Scripts and Limits

Combining Debug with IDA, Ghidra or Binary Ninja generates supplementary JSON and scripts without a type header.
Load symbols first; the script adds strings, comments, runtime caches, field values and references without replacing function names or prototypes.
Without Debug, scripts retain full header and symbol import.
IL2CPP metadata does not provide local stack/register locations, source lines or unwind information, so these are not fabricated.
IDA may revise inferred debug types during decompilation; no script forces user-definite function types.
Anonymous type names can differ between IDA's clang and DWARF importers.

## LLVM References

- `DwarfCompileUnit::addScopeRangeList` and `getOrCreateGlobalVariableDIE`: unit ranges and global locations.
- `DwarfUnit::addDIEEntry`, `DwarfUnit::applySubprogramAttributes` and `DwarfDebug::emitDebugARanges`: local type references, function visibility and address tables.
- Rust's `rustc_codegen_llvm` debuginfo module and `is_function_local_to_unit`: function names, linkage names and local visibility.
- `ELFObjectWriter::computeSymbolTable`: local symbols precede nonlocal symbols and define the symbol table's `sh_info` boundary.
- `MCObjectStreamer::emitDwarfSetLineAddr` and `MCObjectFileInfo::initELFMCObjectFileInfo`: line programs and ELF sections.
- `GnuDebugLinkSection` and `LLVMSymbolizer::findDebugBinary`: companion filename and CRC validation.
