using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Plugins;

namespace Il2CppInspector;

internal sealed class HkrpgMorax : GameMetadataAdapter
{
    private readonly HkrpgMetadata metadata;
    private readonly HkrpgBinary binary;
    private readonly HkrpgUsages usages;

    private HkrpgMorax(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status, GamePlugin plugin)
        : base(plugin)
    {
        binary = new HkrpgBinary(image, global.Length, status);
        metadata = new HkrpgMetadata(global, startup, binary.Header, this, status);
        usages = new HkrpgUsages(metadata, binary, status);
    }

    internal static Il2CppInspector Load(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status, GamePlugin plugin)
    {
        var adapter = new HkrpgMorax(image, global, startup, status, plugin);
        var binary = adapter.binary.Load(adapter.metadata, adapter.usages);
        return new Il2CppInspector(binary, adapter.metadata.Model);
    }

    public override List<MetadataUsage> Usages => usages.Usages;
    public override IEnumerable<AdditionalMetadataUsage> AdditionalUsages => usages.AdditionalUsages;
    public override int VTableSlotSize => 16;

    public override Il2CppTypeDefinitionSizes LayoutSizes(int group) => binary.LayoutSizes(group);
    public override uint LayoutFieldOffset(int group, int local) => binary.LayoutFieldOffset(group, local);
    public override int FieldStorageTag(Reflection.FieldInfo field) => binary.FieldStorageTag(field);
    public override string StorageBase(uint tag) => tag == 0 ? "klass->static_fields" : $"MORAX storage region {tag}";
    internal int DefinitionLayoutGroup(int definition) => binary.DefinitionLayoutGroup(definition);
    internal int LayoutAlignment(int group) => binary.LayoutAlignment(group);

    public override Reflection.TypeInfo ResolveGenericType(Reflection.TypeModel model, Il2CppType type) => binary.ResolveGenericType(model, type);
    public override (ulong Address, object Value) DecodeDefault(int typeIndex, int dataIndex, Il2CppBinary binary) =>
        HkrpgDefaults.Decode(metadata, typeIndex, dataIndex, binary);
    public override int FieldRvaSize(Il2CppType type, Il2CppBinary binary) => HkrpgDefaults.FieldRvaSize(type, binary);
}
