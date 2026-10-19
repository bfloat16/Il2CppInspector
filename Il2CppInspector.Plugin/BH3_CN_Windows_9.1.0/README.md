# BH3_CN_Windows_9.1.0

Decodes the Windows 9.1.0 MORAX records from `UserAssembly.dll`,
`global-metadata.dat` and `startup-metadata.dat`. Supply the exact game ID
because other MORAX plugins can match the same metadata signature.

```bash
dotnet build Il2CppInspector.slnx -c Release
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll --game BH3_CN_Windows_9.1.0 -i '<Native>/UserAssembly.dll' -m '<Native>/global-metadata.dat' -o '<output>' -t IDA
```

The plugin normalizes records for the shared reflection and export pipeline.
Runtime header overrides retain the separate vtable pointer and encoded
runtime fields. Runtime caches are emitted separately from standard FieldRVA
usages. Generic layouts reuse the Common sharing rules.

Real-input checks and optional header output are documented in
[BH3 tests](../../Il2CppInspector.Tests/Plugin/BH3/README.md).
