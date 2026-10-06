# Game Plugins

Game implementations live under `Il2CppInspector.Plugin/<game>/`, with one project and managed plugin DLL per game.

Common owns plugin, metadata, layout and analysis contracts; it does not reference game projects.
Game directories own decoding, keys, runtime layouts and specialized exports.

The CLI discovers `Il2CppInspector.Plugin.*.dll` beside the executable or under `plugins/`.
`McMaster.NETCore.Plugins` shares host types while keeping plugin dependency resolution separate.
Each loaded package retains its plugin instance, so loading another game does not change existing models.

```bash
dotnet build Il2CppInspector.slnx -c Release
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll --game NAME_REGION_VERSION -i GameAssembly.dll -m global-metadata.dat -o output/game -t IDA
```

`--game NAME_REGION_VERSION` selects an installed plugin's exact supported ID, case-insensitively.
Without it, metadata detection selects the plugin; ambiguous matches require an explicit ID, while ordinary metadata uses the stock loader.

## Adding a Game

1. Add `<game>/Il2CppInspector.Plugin.<game>.csproj` targeting .NET 10 with `EnableDynamicLoading=true`, a Common reference and the default assembly name.
2. Implement a public, parameterless `GamePlugin` subclass with a supported `NAME_REGION_VERSION` identifier.
3. Decode stock metadata/binary records and attach a `GameMetadataAdapter` through `Metadata.CreateForPlugin`; game projects receive friend access while public record setters remain read-only.
4. Implement layout, analysis and output hooks, setting `StreamExports` for lazy streamed exports.
5. Add the project to the solution and tests for the supported inputs.

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
