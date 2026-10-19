# Endfield_CN_Android_x.x.x

Ports the supplied `GlobalMetadata.cs` repair for decoded v29 metadata with a
0x108-byte header. The plugin removes the extra 8 bytes, shifts section offsets,
rebuilds Windows Runtime type names after exported type definitions, and clears
Windows Runtime strings. It also normalizes the custom 92-byte type definitions
to stock 88-byte v29 records by removing the extra word at byte 64 and adjusting
subsequent section offsets. The image type ranges determine the expected type
count; stock-sized type definitions are accepted without conversion.
Input files are left intact; repaired metadata is loaded
in memory through the stock reader, binary registration discovery and exports.

```bash
dotnet build Il2CppInspector.slnx -c Release
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll --game Endfield_CN_Android_x.x.x -i libil2cpp.so -m global-metadata.dat -o output/Endfield_CN_Android_x.x.x -t IDA --unity-version 2021.3.0f1
```

Set `--unity-version` to the actual player version when exporting C++ headers.
No player version is assumed by this metadata-only repair. Automatic detection
requires the stock metadata magic, version 29 and string literal offset 0x108.
Already repaired stock metadata is handled by the stock loader without `--game`.
Encrypted metadata must be decoded first, as with the supplied script.

Regression checks run with `dotnet run --project Il2CppInspector.Tests -c Release -- --plugins`.
