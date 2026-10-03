namespace Il2CppInspector.Tests.Common.Reflection
{
    internal static class TypeModelTests
    {
        internal static void VerifyGenericEnums(TypeModel stockModel)
        {
            var genericEnum = stockModel.TypesByDefinitionIndex.FirstOrDefault(t => t.IsEnum && t.IsGenericTypeDefinition);
            if (genericEnum != null)
            {
                var instance = genericEnum.MakeGenericType(genericEnum.GetGenericArguments().Select(_ => stockModel.TypesByFullName["System.Object"]).ToArray());
                Check(instance.IsEnum && instance.GetEnumUnderlyingType() == genericEnum.GetEnumUnderlyingType(), "Stock constructed nested enums retain their scalar identity");
            }
        }
    }
}
