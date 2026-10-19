# Tears of Themis OS 5.8.1 Android

Game ID: `TOT_OS_Android_5.8.1`. Inputs: `libil2cpp.so` and encrypted `global-metadata.dat`; `libunity.so` is not required at runtime.

The plugin recognizes this build's 39919768-byte metadata and the trailing keybox gate. CN 6.1.0 uses a different size, so automatic selection remains unambiguous.

Decoding, v24.1 record readers and layout hooks are compiled from the CN project's shared source. The OS project embeds its own copy of the identical white-box resource and supplies its own `TotBuild` constants; it has no runtime dependency on the CN plugin DLL. Standard Common analysis and inherited output writers handle the normalized records.

OS-specific values verified against the sample:

| Parameter | Value |
| --- | --- |
| CBC unit stride | `0x26100` |
| CBC IV | `6895c531457fe0b8a0f561521d33a584` |
| Literal index table | `0xEDB80`, 36922 entries |
| Literal data | `0x158`, 973352 bytes |
| Name table | `0xA20A70` |

The IV was recovered from 23 consistent MethodDef token constraints. White-box Td0..3, index and final-round tables are byte-identical to CN 6.1.0; the existing tail KDF reads the OS record offset `0x177B` from the keybox itself. No game/version selection branch is added to Common or the CLI.

```bash
dotnet build Il2CppInspector.slnx -c Release
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll --game TOT_OS_Android_5.8.1 -i '<Native>/libil2cpp.so' -m '<Native>/global-metadata.dat' -o output/tot-os -t IDA
dotnet run --project Il2CppInspector.Tests -c Release -- --tot-os '<Native>'
```

See [OS tests](../../Il2CppInspector.Tests/Plugin/TOT/OS/README.md) for the independent plaintext hashes and full-load/export regression.
