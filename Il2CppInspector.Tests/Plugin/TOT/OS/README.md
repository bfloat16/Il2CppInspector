# TOT OS Tests

`--plugins` checks discovery, case-insensitive selection, independent CN/OS matching, damaged gates and truncated inputs without external samples.

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --plugins
dotnet run --project Il2CppInspector.Tests -c Release -- --tot-os '<OS 5.8.1 Native directory>'
```

The real-input mode requires `libil2cpp.so` and `global-metadata.dat`. It checks independent Python plaintext hashes, input preservation, all table counts, names/literals, automatic OS selection, native registration parsing and standard JSON/C++/pointer/DummyDll exports. Temporary exports are deleted after validation.

| Fixture | SHA-256 |
| --- | --- |
| Encrypted metadata | `feed68781090f44c6c09cb0bea1e7d596f7342b55b618cbeaba5672a520b15de` |
| Outer plaintext | `cbcfc0a4c07a74c43747a1250533051fb37362efdfad545a68c14433cd3959b1` |
| Full plaintext | `e63cf3f1918b530bf6c3d6e2ba05954ec358e8e730b708047abfcfb5b0db0349` |

The OS project reference uses the `tot_os` assembly alias so these tests can inspect the shared internal decoder types in the OS assembly independently of their CN counterparts.
