using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugins
{
    // TypeIndex == -1 denotes native data without a corresponding managed type reference.
    public sealed record AdditionalMetadataUsage(string Section, int TypeIndex, ulong Address, string Name, string Type)
    {
        public int? Length { get; init; }
        public string Value { get; init; }
        public uint? Subtype { get; init; }
    }

    public abstract class GameMetadataAdapter(GamePlugin plugin)
    {
        public GamePlugin Plugin { get; } = plugin;
        public abstract List<MetadataUsage> Usages { get; }
        public virtual int VTableSlotSize => 8;

        public virtual TypeInfo ResolveGenericType(TypeModel model, Il2CppType type) => null;

        public virtual int FieldStorageTag(FieldInfo field) => 0;

        public virtual Il2CppTypeDefinitionSizes LayoutSizes(int group) => default;

        public virtual uint LayoutFieldOffset(int group, int local) => 0;

        public abstract (ulong Address, object Value) DecodeDefault(int typeIndex, int dataIndex, Il2CppBinary binary);

        public virtual int FieldRvaSize(Il2CppType type, Il2CppBinary binary) => 0;

        public virtual string StorageBase(uint tag) => null;

        public virtual IEnumerable<AdditionalMetadataUsage> AdditionalUsages => [];

        /// <summary>Offset of a normalized serialized attribute blob, or -1 to use the metadata header.</summary>
        public virtual int AttributeDataOffset => -1;
    }
}
