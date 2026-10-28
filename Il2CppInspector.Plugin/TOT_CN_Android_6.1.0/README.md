# TOT_CN_Android_6.1.0

Inputs: `libil2cpp.so` and the encrypted `global-metadata.dat`.

The plugin recognizes the 39,779,644-byte metadata and its trailing keybox gate, decrypts both layers, reads the permuted header and normalizes the records to stock v24.1.

`TotBuild.cs` holds the build-specific framing and table offsets. [OS 5.8.1](../TOT_OS_Android_5.8.1/README.md) links all other source and the white-box table resource.
