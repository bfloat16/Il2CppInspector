# Tears of Themis CN 6.1.0 Android

Game ID: `TOT_CN_Android_6.1.0`. Inputs: `libil2cpp.so` and encrypted `global-metadata.dat`.

The plugin recognizes this build's 39779644-byte metadata and the trailing keybox gate. It decrypts two layers, reads the permuted header and normalizes stock v24.1 records before using standard analysis and exports.

`TotBuild.cs` owns build-specific framing and table offsets. The [OS 5.8.1 project](../TOT_OS_Android_5.8.1/README.md) links the common TOT decoding/record/model source and the identical table resource into its own DLL, with its own constants and plugin entry point.

See [TOT tests](../../Il2CppInspector.Tests/Plugin/TOT/README.md) for real-input validation.
