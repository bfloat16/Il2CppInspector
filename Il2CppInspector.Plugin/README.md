# Game Plugins

Each `Il2CppInspector.Plugin/<GameId>/` directory builds one plugin, `Il2CppInspector.Plugin.<GameId>.dll`. Plugins decode game metadata and binaries and supply binary-verified runtime layout differences. Common owns the plugin contracts, reflection, declaration generation and all outputs, and references no game project.

The CLI loads `Il2CppInspector.Plugin.*.dll` from beside the executable or under `plugins/` through `McMaster.NETCore.Plugins`, which shares host types and keeps plugin dependencies separate. Each loaded package keeps its own plugin instance.

```bash
dotnet build Il2CppInspector.slnx -c Release
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll --game NAME_REGION_PLATFORM_VERSION -i GameAssembly.dll -m global-metadata.dat -o output/game -t IDA
```

`--game` takes an exact ID, case-insensitively; version `x.x.x` means the plugin supports several game versions. Without `--game`, metadata detection selects the plugin. Ambiguous matches require `--game`, and unmatched metadata uses the stock loader.

| Game | Supported ID |
| --- | --- |
| BH3 | `BH3_CN_Windows_9.1.0` |
| Genshin | `Genshin_CN_Windows_7.1.0` |
| HSR | `HSR_OS_Windows_4.6.0`, `HSR_CN_Windows_4.6.51` |
| ZZZ | `ZZZ_CN_Windows_3.2.0`, `ZZZ_CN_Windows_3.3.0` |
| Endfield | `Endfield_CN_Android_x.x.x`, `Endfield_CN_Windows_1.6.3` |
| TOT | `TOT_CN_Android_6.1.0`, `TOT_OS_Android_5.8.1` |
| Love and Deepspace | `LAD_CN_Android_6.0.0` |

## Adding a Game

1. Create `<GameId>/Il2CppInspector.Plugin.<GameId>.csproj` targeting .NET 10 with `EnableDynamicLoading=true`, a Common reference and `RootNamespace` set to `Il2CppInspector.Plugin.<game>`. Add it to the solution under `/Plugins/<game>/`.
2. Implement a public, parameterless `GamePlugin` subclass.
3. Normalize decoded records to stock records and attach a `GameMetadataAdapter` through `Metadata.CreateForPlugin`. Common's internal members are visible to plugin projects.
4. Supply layout hooks only where the runtime layout differs from stock; `NativeLayoutAnalysisModel` provides method traversal and declaration naming. Changes to field widths, cached records or vtable representation need evidence from the binary.
5. Keep the stock export names, without prefixes such as `Zzz_`, and do not write separate DLL, header or JSON writers.
6. Add a row to `Samples` in `Il2CppInspector.Tests/Plugin/GameSampleTests.cs`.

MORAX games (BH3, Genshin, HSR, ZZZ) build on `Shared/Morax/` (`MoraxPlugin`, `MoraxAdapter`, `MoraxNativeModel`, `MoraxReader` and the runtime-cache script), imported through `Shared/Morax/Morax.props`. Their directories hold only keys, offsets, record readers and verified layout differences.

A version that shares source with another links the files from that version's directory instead of copying them (see `TOT_OS_Android_5.8.1`, `ZZZ_CN_Windows_3.3.0` and `HSR_CN_Windows_4.6.51`).

ZZZ keeps its class layout adapter because `0x19F266500` reads precomputed layout descriptors and allocates one or two vtable arrays, and `0x180277C50` allocates both arrays for constructed classes.

Build and publish copy each plugin and its dependencies to `plugins/<GameId>/`; adding a game needs no CLI change.

## Checks

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --plugins
dotnet run --project Il2CppInspector.Tests -c Release -- --plugin-cli '<published CLI DLL>' '<isolated directory>'
dotnet run --project Il2CppInspector.Tests -c Release -- --stock '<Unity player directory>'
dotnet run --project Il2CppInspector.Tests -c Release -- --game <GameId> <sample Native directory> [output directory]
```

Every plugin uses the same checks; do not add game-specific test files or modes.
