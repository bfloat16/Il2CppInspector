using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugins
{
    public sealed record AdditionalMetadataUsage(string Section, int TypeIndex, ulong Address, string Name, string Type);

    public abstract class GameMetadataAdapter
    {
        protected GameMetadataAdapter(GamePlugin plugin) => Plugin = plugin;

        public GamePlugin Plugin { get; }
        public abstract List<MetadataUsage> Usages { get; }
        public abstract int VTableSlotSize { get; }

        public abstract TypeInfo ResolveGenericType(TypeModel model, Il2CppType type);
        public abstract int FieldStorageTag(FieldInfo field);
        public abstract Il2CppTypeDefinitionSizes LayoutSizes(int group);
        public abstract uint LayoutFieldOffset(int group, int local);
        public abstract (ulong Address, object Value) DecodeDefault(int typeIndex, int dataIndex, Il2CppBinary binary);
        public abstract int FieldRvaSize(Il2CppType type, Il2CppBinary binary);
        public abstract string StorageBase(uint tag);
        public virtual IEnumerable<AdditionalMetadataUsage> AdditionalUsages => [];
    }
}
