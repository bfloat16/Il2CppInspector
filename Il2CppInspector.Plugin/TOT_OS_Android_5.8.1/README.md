# TOT_OS_Android_5.8.1

Inputs: `libil2cpp.so` and the encrypted `global-metadata.dat`; `libunity.so` is not needed.

The plugin recognizes the 39,919,768-byte metadata and its trailing keybox gate. CN 6.1.0 has a different size, so automatic selection is unambiguous.

The source and white-box table resource are linked from [CN 6.1.0](../TOT_CN_Android_6.1.0/README.md); only `TotBuild.cs` differs. The OS values, verified against the sample:

| Parameter | Value |
| --- | --- |
| CBC unit stride | `0x26100` |
| CBC IV | `6895c531457fe0b8a0f561521d33a584` |
| Literal index table | `0xEDB80`, 36922 entries |
| Literal data | `0x158`, 973352 bytes |
| Name table | `0xA20A70` |

The IV was recovered from 23 consistent MethodDef token constraints. The white-box Td0..3, index and final-round tables are byte-identical to CN 6.1.0. The tail KDF reads the OS record offset `0x177B` from the keybox itself.

```bash
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll --game TOT_OS_Android_5.8.1 -i '<Native>/libil2cpp.so' -m '<Native>/global-metadata.dat' -o output/tot-os -t IDA
```
