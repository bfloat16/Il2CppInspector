# Game Plugins

Game implementations live under `Il2CppInspector.Plugin/<game>/`, with one project and managed plugin DLL per game.

Common owns plugin, metadata, layout and analysis contracts; it does not reference game projects.
Game directories own decoding, keys and binary-verified runtime layout differences. Common owns reflection, declaration generation, dependency ordering and exports.

The CLI discovers `Il2CppInspector.Plugin.*.dll` beside the executable or under `plugins/`.
`McMaster.NETCore.Plugins` shares host types while keeping plugin dependency resolution separate.
Each loaded package retains its plugin instance, so loading another game does not change existing models.

```bash
dotnet build Il2CppInspector.slnx -c Release
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll --game NAME_REGION_PLATFORM_VERSION -i GameAssembly.dll -m global-metadata.dat -o output/game -t IDA
```

`--game NAME_REGION_PLATFORM_VERSION` selects an installed plugin's exact supported ID, case-insensitively.
The literal version `x.x.x` identifies a plugin that supports multiple game versions.
Without it, metadata detection selects the plugin; ambiguous matches require an explicit ID, while ordinary metadata uses the stock loader.

| Game | Supported ID |
| --- | --- |
| BH3 | `BH3_CN_Windows_9.1.0` |
| Genshin | `Genshin_CN_Windows_7.1.0` |
| ZZZ | `ZZZ_CN_Windows_3.2.0` |
| HSR (CN Windows 4.6.51 beta) | `HSR_CN_Windows_4.6.51` |
| Endfield | `Endfield_CN_Android_x.x.x` |
| TOT | `TOT_CN_Android_6.1.0` |


## Adding a Game

1. Add `<game>/Il2CppInspector.Plugin.<game>.csproj` targeting .NET 10 with `EnableDynamicLoading=true`, a Common reference and the default assembly name.
2. Implement a public, parameterless `GamePlugin` subclass with a supported `NAME_REGION_PLATFORM_VERSION` identifier.
3. Decode stock metadata/binary records and attach a `GameMetadataAdapter` through `Metadata.CreateForPlugin`; game projects receive friend access while public record setters remain read-only.
4. Reuse the stock analysis and outputs after normalization. If the binary has a different runtime layout, supply only its layout hooks; `NativeLayoutAnalysisModel` shares the stock method traversal and declaration naming. `StreamExports` keeps reflection data lazy.
5. Add the project to the solution and tests for the supported inputs.

Exports use the stock Il2CppInspector naming rules. Preserve metadata type, method and field names; do not add game or plugin prefixes such as `Zzz_`. Specialized native models should share the stock type namer so runtime names, keywords and duplicate identifiers are handled consistently across output formats.

Normalize reordered or encrypted records into the stock model before exporting. Runtime header overrides preserve stock member names and semantic types wherever the binary permits; document encoding on the affected members. Changes to field widths, cached records or vtable representation require evidence from the supported binary. Do not implement separate DLL, header or JSON writers for reordered/encrypted metadata.

ZZZ uses `AssemblyShims`, `CppScaffolding` and `JSONMetadata` directly. Its remaining adapters cover the decoded registration/metadata records, descriptor lookup and class/vtable layout. `0x19F266500` reads precomputed layout descriptors and allocates one or two vtable arrays; `0x180277C50` allocates both arrays for constructed classes. These verified differences prevent simply suppressing the adapter as TOT does. The ordinary generic sharing rules live in Common, not in the game plugin.

Build/publish packaging includes `Il2CppInspector.Plugin/*/*.csproj` automatically under `plugins/<game>/`, with dependency files.
Adding a game does not require a CLI switch or decoding branch in Common.

## Checks

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --plugins
dotnet run --project Il2CppInspector.Tests -c Release -- --plugin-cli '<published CLI DLL>' '<isolated directory>'
dotnet run --project Il2CppInspector.Tests -c Release -- --stock '<Unity player directory>'
```

Place tests under `Il2CppInspector.Tests/Plugin/<game>/`, mirroring the plugin's modules.
Document real local inputs and additional modes beside those tests.
