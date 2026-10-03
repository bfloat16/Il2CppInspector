using Il2CppInspector.Next.BinaryMetadata;

namespace Il2CppInspector.Reflection
{
    // Matches GetSharedGenericInst / GetSharedType before the runtime's layout lookup
    // (sub_18028E0E0 -> sub_18028D670 -> sub_18028D410). Type identity stays unchanged.
    internal sealed class ZzzSharedTypeLayouts : Plugins.IGameTypeLayouts
    {
        private readonly TypeModel model;
        private readonly ZzzMorax adapter;
        private readonly TypeInfo objectType;
        private readonly Dictionary<TypeInfo, TypeInfo> sharedArguments = [];
        public Dictionary<TypeInfo, int> GenericOrdinals { get; } = [];
        public Dictionary<int, TypeInfo> GenericTypes { get; } = [];

        internal ZzzSharedTypeLayouts(TypeModel model)
        {
            this.model = model;
            adapter = (ZzzMorax)model.Package.Metadata.GameAdapter;
            objectType = model.TypesByFullName["System.Object"];
            for (var i = 0; i < model.Package.TypeReferences.Length; i++)
            {
                var raw = model.Package.TypeReferences[i];
                if (raw.Type != Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST || raw.ByRef)
                {
                    continue;
                }

                var type = model.TypesByReferenceIndex[i];
                if (type == null)
                {
                    continue;
                }

                var ordinal = checked((int)raw.Data.Value);
                GenericOrdinals.TryAdd(type, ordinal);
                GenericTypes.TryAdd(ordinal, type);
            }
        }

        public void Bind(TypeInfo type)
        {
            if (type.IsEnum)
            {
                type.SetRuntimeSizes(type.GetEnumUnderlyingType().Sizes);
                return;
            }
            if (type.ContainsGenericParameters)
            {
                return;
            }

            if (TryGroup(type, out var group))
            {
                type.SetGameLayoutGroup(group);
                return;
            }
            var args = type.GetGenericArguments();
            var shared = args.Select(SharedArgument).ToArray();
            if (args.SequenceEqual(shared))
            {
                return;
            }

            var sharedType = type.GetGenericTypeDefinition().MakeGenericType(shared);
            if (TryGroup(sharedType, out group))
            {
                type.SetGameLayoutGroup(group);
            }
        }

        private bool TryGroup(TypeInfo type, out int group)
        {
            group = 0;
            return GenericOrdinals.TryGetValue(type, out var ordinal) && adapter.GenericLayoutGroups.TryGetValue(ordinal, out group);
        }

        private TypeInfo SharedArgument(TypeInfo type)
        {
            // Only reference kinds are replaced with Object. Native pointers and
            // byrefs are not managed reference types in the runtime's kind mask.
            if (!type.IsValueType && !type.IsPointer && !type.IsByRef && !type.IsGenericParameter)
            {
                return objectType;
            }

            if (type.IsEnum && adapter.EnableEnumSharing)
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
}
