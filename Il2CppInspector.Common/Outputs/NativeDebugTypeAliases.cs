using Il2CppInspector.Cpp;

namespace Il2CppInspector.Outputs;

// Match the array-length view used by CppScaffolding(useBetterArraySize: true).
internal sealed class NativeDebugTypeAliases
{
    private CppComplexType arraySize;

    public CppType AliasElement(CppAlias alias) => alias is { Name: "il2cpp_array_size_t", SizeBytes: 8 } ? ArraySize(alias.ElementType) : alias.ElementType;

    public CppType FieldType(CppComplexType owner, CppType type, bool isConst = false)
    {
        if (isConst)
            type = PrefixConst(type);
        if (owner.Group != "il2cpp" && owner.CCompatibleEnumFields && type is CppEnumType enumeration)
            return enumeration.UnderlyingType;
        if (type is not CppAlias { Name: "il2cpp_array_size_t", SizeBytes: 8 } alias)
            return type;
        if (owner.Group == "il2cpp")
            return alias.ElementType.AsAlias("actual_il2cpp_array_size_t");
        return ArraySize(alias.ElementType).AsAlias(alias.Name);
    }

    private CppComplexType ArraySize(CppType element)
    {
        if (arraySize == null)
        {
            arraySize = new(ComplexValueType.Union) { Name = "better_il2cpp_array_size_t" };
            arraySize.AddField("size", new CppType("int32_t", 32));
            arraySize.AddField("value", element.AsAlias("actual_il2cpp_array_size_t"));
        }
        return arraySize;
    }

    private static CppType PrefixConst(CppType type) =>
        type switch
        {
            CppPointerType pointer => PrefixConst(pointer.ElementType).AsPointer(pointer.Size),
            CppArrayType array => PrefixConst(array.ElementType).AsArray(array.Length),
            _ => type.AsConst(),
        };
}
