using Il2CppInspector.Cpp;
using Il2CppInspector.Model;
using Il2CppInspector.Next;

namespace Il2CppInspector.Outputs;

internal sealed record NativeDebugVariable(string Name, ulong Address, CppType Type);

internal static class NativeDebugGlobals
{
    public static IEnumerable<NativeDebugVariable> Enumerate(AppModel model)
    {
        var cpp = model.RuntimeCppTypes;
        var binary = model.Package.Binary;
        var reflection = model.TypeModel;
        CppType Pointer(string name) => cpp[name]?.AsPointer(model.WordSizeBits);

        yield return new("g_CodeRegistration", binary.CodeRegistrationPointer, cpp["Il2CppCodeRegistration"]);
        yield return new("g_MetadataRegistration", binary.MetadataRegistrationPointer, cpp["Il2CppMetadataRegistration"]);
        foreach (var (name, address) in binary.CodeGenModulePointers)
            yield return new($"g_{name.Replace(".dll", "")}CodeGenModule", address, cpp["Il2CppCodeGenModule"]);
        if (model.Package.Version >= MetadataVersions.V242 && binary.Modules.Count > 0)
            yield return new("g_CodeGenModules", binary.CodeRegistration.CodeGenModules, Pointer("Il2CppCodeGenModule")?.AsArray(binary.Modules.Count));

        // Enumerate every cache slot, not AppType/AppMethod's single retained address.
        foreach (var usage in model.Package.MetadataUsages ?? [])
        {
            switch (usage.Type)
            {
                case MetadataUsageType.TypeInfo:
                    var type = reflection.GetMetadataUsageType(usage);
                    if (type == null)
                        break;
                    var className = model.GameNativeModel?.ClassName(type);
                    if (className == null && model.AnalysisTypes.TryGetValue(type, out var appType))
                        className = appType.Name + "__Class";
                    var klass = (className == null ? null : cpp[className]) ?? cpp["Il2CppClass"];
                    yield return new(MangledNameBuilder.TypeInfo(type), usage.VirtualAddress, klass?.AsPointer(model.WordSizeBits));
                    break;
                case MetadataUsageType.Type:
                    type = reflection.GetMetadataUsageType(usage);
                    if (type != null)
                        yield return new(MangledNameBuilder.TypeRef(type), usage.VirtualAddress, Pointer("Il2CppType"));
                    break;
                case MetadataUsageType.MethodDef:
                case MetadataUsageType.MethodRef:
                    var method = reflection.GetMetadataUsageMethod(usage);
                    yield return new(MangledNameBuilder.MethodInfo(method), usage.VirtualAddress, Pointer("MethodInfo"));
                    break;
                case MetadataUsageType.StringLiteral:
                    yield return new($"StringLiteral_{usage.SourceIndex}_{usage.VirtualAddress:X}", usage.VirtualAddress, Pointer("Il2CppString"));
                    break;
                case MetadataUsageType.FieldInfo:
                    yield return new($"FieldInfo_{usage.SourceIndex}_{usage.VirtualAddress:X}", usage.VirtualAddress, Pointer("FieldInfo"));
                    break;
                case MetadataUsageType.FieldRva:
                    // This is an initialized-data pointer cache, not the inline field value.
                    yield return new($"FieldRva_{usage.SourceIndex}_{usage.VirtualAddress:X}", usage.VirtualAddress, Pointer("void"));
                    break;
            }
        }
    }
}
