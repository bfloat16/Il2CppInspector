namespace Il2CppInspector.Reflection;

// IL2CPP GetSharedType/GetSharedGenericInst: preserve identity; normalize only layout lookup keys.
internal sealed class GenericTypeSharing(TypeModel model, bool enableEnumSharing)
{
    private readonly TypeInfo objectType = model.TypesByFullName["System.Object"];
    private readonly Dictionary<TypeInfo, TypeInfo> sharedArguments = [];

    internal TypeInfo SharedArgument(TypeInfo type)
    {
        // Only reference kinds are replaced with Object. Native pointers and
        // byrefs are not managed reference types in the runtime's kind mask.
        if (!type.IsValueType && !type.IsPointer && !type.IsByRef && !type.IsGenericParameter)
        {
            return objectType;
        }

        if (type.IsEnum && enableEnumSharing)
        {
            return model.TypesByFullName["System." + type.GetEnumUnderlyingType().Name + "Enum"];
        }

        if (!type.IsGenericType || type.IsGenericTypeDefinition)
        {
            return type;
        }

        if (sharedArguments.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var args = type.GetGenericArguments();
        var shared = args.Select(SharedArgument).ToArray();
        var result = args.SequenceEqual(shared) ? type : type.GetGenericTypeDefinition().MakeGenericType(shared);
        sharedArguments.Add(type, result);
        return result;
    }
}
