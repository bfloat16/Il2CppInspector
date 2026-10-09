# Endfield_CN_Android_x.x.x Checks

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --plugins
```

Fixtures cover plugin discovery, the 0x108-to-0x100 header repair, all section
offsets, invalid ranges, and 92-to-88-byte type record normalization. The custom
word precedes member counts at byte 64. Image ranges are checked against the
type table size to avoid silently reading truncated stock records.

## Real Sample

Validated on 2026-10-07 using these existing tprt-restored inputs:

- `H:/Project/Game/CN/明日方舟：终末地/1.5.3_Android/Native/libil2cpp.clean.so`
- `H:/Project/Game/CN/明日方舟：终末地/1.5.3_Android/Native/global-metadata.clean.dat`

```bash
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll --game Endfield_CN_Android_x.x.x -i '<Native>/libil2cpp.clean.so' -m '<Native>/global-metadata.clean.dat' -t IDA -o '<temporary output>'
```

The type table contains 58,328 records of 92 bytes. Reading it as stock v29
previously produced 60,979 types, preventing metadata registration discovery.
After normalization, stock discovery finds CodeRegistration at `0x19356578`
and MetadataRegistration at `0x19BC39A8`. The full CLI run completes type
reflection, assembly shims, C++ headers, JSON and IDA script generation.
The SO was inspected with structured ELF parsing and LLVM disassembly;
it was not loaded into IDA.
