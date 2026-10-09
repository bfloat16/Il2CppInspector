using Il2CppInspector.Next.BinaryMetadata;

namespace Il2CppInspector.Reflection
{
    // Standard IL2CPP generic sharing applied to recorded native layout groups. Identity and
    // construction stay in TypeModel; only the layout lookup arguments are normalized.
    internal sealed class SharedTypeLayouts : Plugins.IGameTypeLayouts
    {
        private readonly IReadOnlyDictionary<int, int> layoutGroups;
        private readonly GenericTypeSharing sharing;
        public Dictionary<TypeInfo, int> GenericOrdinals { get; } = [];
        public Dictionary<int, TypeInfo> GenericTypes { get; } = [];

        internal SharedTypeLayouts(TypeModel model, IReadOnlyDictionary<int, int> layoutGroups, bool enableEnumSharing)
        {
            this.layoutGroups = layoutGroups;
            sharing = new GenericTypeSharing(model, enableEnumSharing);
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
            var shared = args.Select(sharing.SharedArgument).ToArray();
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
            return GenericOrdinals.TryGetValue(type, out var ordinal) && layoutGroups.TryGetValue(ordinal, out group);
        }
    }
}
