# Game Plugins

Game implementations live under `Il2CppInspector.Plugin/<game>/`, with one project and managed plugin DLL per game.

The Common library owns the `GamePlugin`, `GameMetadataAdapter`, layout and analysis contracts. Game plugins reference Common; Common has no project reference to any game. General model fixes and streaming infrastructure stay in Common. Game decoding, keys, runtime layouts and specialized output belong to the game directory.

The CLI discovers `Il2CppInspector.Plugin.*.dll` beside the executable or under its `plugins/` directory. `McMaster.NETCore.Plugins` loads each plugin with shared host types and separate dependency resolution. Plugin instances are retained for the process lifetime; the selected instance travels with each loaded metadata package, so loading another game does not change old models.

```bash
dotnet build Il2CppInspector.slnx -c Release
dotnet Il2CppInspector.CLI/bin/Release/net10.0/Il2CppInspector.dll --game NAME_REGION_VERSION -i GameAssembly.dll -m global-metadata.dat -o output/game -t IDA
```

`--game NAME_REGION_VERSION` selects an exact supported ID, case-insensitively. Replace `NAME_REGION_VERSION` with a game name, region and version supported by an installed plugin. Without it, plugins inspect the metadata; ambiguous matches require explicit selection. Unmatched ordinary metadata uses the stock loader.

## Adding a Game

1. Add `<game>/Il2CppInspector.Plugin.<game>.csproj`, targeting .NET 10 with `EnableDynamicLoading=true` and a reference to Common. Keep its default assembly name equal to the project filename.
2. Implement a public, parameterless `GamePlugin` subclass and return each supported `NAME_REGION_VERSION` from its `GameId`. Different builds can have separate plugin subclasses in the same DLL.
3. Decode input into stock metadata/binary records. The repository grants friend access to game projects automatically; public record setters remain read-only. Attach a `GameMetadataAdapter` using `Metadata.CreateForPlugin`.
4. Implement layout/reflection, public analysis model and output hooks. Set `StreamExports` when exports use lazy methods and streamed output. Optional startup metadata discovery and Python metadata processing are plugin hooks.
5. Add the project to the solution and tests for the supported inputs.

CLI project references and build/publish packaging automatically include projects matching `Il2CppInspector.Plugin/*/*.csproj`. Each output is placed in `plugins/<game>/`, together with its dependency files. Additional games do not require a new game switch in CLI or hardcoded decoding branches in Common.

## Checks

```bash
dotnet run --project Il2CppInspector.Tests -c Release -- --plugins
dotnet run --project Il2CppInspector.Tests -c Release -- --plugin-cli '<published CLI DLL>' '<isolated directory>'
dotnet run --project Il2CppInspector.Tests -c Release -- --stock '<Unity player directory>'
```

Place game-specific decoding and output tests under `Il2CppInspector.Tests/Plugin/<game>/`, mirroring the plugin's module folders. Supply real input files locally and document any additional test modes alongside the corresponding game tests.
