# Genshin_CN_Windows_7.1.0

Decodes the Windows 7.1.0 MORAX records from `YuanShen.exe`,
`global-metadata.dat` and `startup-metadata.dat`. Supply the exact game ID
because other MORAX plugins can match the same metadata signature.

```bash
dotnet build Il2CppInspector.slnx -c Release
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll --game Genshin_CN_Windows_7.1.0 -i '<Native>/YuanShen.exe' -m '<Native>/global-metadata.dat' -o '<output>' -t IDA
```

The plugin restores shuffled and encrypted fields for the shared reflection
and export pipeline. Runtime headers preserve the recorded physical layouts.
Native strings and empty-array caches retain their own output sections.
Generic layouts reuse the Common sharing rules.

Real-input checks and optional exports are documented in
[Genshin tests](../../Il2CppInspector.Tests/Plugin/Genshin/README.md).
