using Il2CppInspector.Next.BinaryMetadata;

namespace Il2CppInspector.Reflection
{
    // Matches GetSharedGenericInst / GetSharedType before the runtime's layout lookup
    // (sub_18028E0E0 -> sub_18028D670 -> sub_18028D410). Type identity stays unchanged.
    internal sealed class ZzzSharedTypeLayouts : Plugins.IGameTypeLayouts
    {
        private readonly TypeModel model;
        private readonly ZzzMorax adapter;
        private readonly GenericTypeSharing sharing;
        public Dictionary<TypeInfo, int> GenericOrdinals { get; } = [];
        public Dictionary<int, TypeInfo> GenericTypes { get; } = [];

        internal ZzzSharedTypeLayouts(TypeModel model)
        {
            this.model = model;
            adapter = (ZzzMorax)model.Package.Metadata.GameAdapter;
            sharing = new GenericTypeSharing(model, adapter.EnableEnumSharing);
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
            return GenericOrdinals.TryGetValue(type, out var ordinal) && adapter.GenericLayoutGroups.TryGetValue(ordinal, out group);
        }
    }
}
