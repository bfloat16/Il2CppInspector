using System.Text.RegularExpressions;

namespace Il2CppInspector;

internal static class HkrpgRuntimeHeaders
{
    internal static string Apply(string stock)
    {
        // morax's source explicitly marks native ABI layouts as unverified. Do not emit stock
        // class/member layouts as though they represented the encrypted runtime structures.
        foreach (var name in new[] { "Il2CppType", "Il2CppGenericClass", "Il2CppClass", "Il2CppClass_0", "Il2CppClass_1", "Il2CppClass_Merged", "MethodInfo", "FieldInfo", "PropertyInfo", "EventInfo", "Il2CppImage", "Il2CppAssembly", "Il2CppAssemblyName", "Il2CppCodeRegistration", "Il2CppMetadataRegistration" })
        {
            var pattern = $@"typedef struct (?:__attribute__\(\(aligned\([0-9]+\)\)\)\s+)?{name}\s*\{{.*?\}}\s*{name};";
            if (!Regex.IsMatch(stock, pattern, RegexOptions.Singleline))
                throw new InvalidOperationException($"Missing runtime header record {name}.");
            stock = Regex.Replace(stock, pattern, $"typedef struct {name} {name};", RegexOptions.Singleline);
        }
        return "// HSR 4.6.51: metadata-derived fields; runtime class/member ABI is opaque and unverified.\n" + stock;
    }
}
