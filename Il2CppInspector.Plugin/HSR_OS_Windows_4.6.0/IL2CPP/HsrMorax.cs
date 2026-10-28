using System.Collections.Immutable;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Plugins;

namespace Il2CppInspector;

internal sealed class HsrMorax : MoraxAdapter
{
    private readonly HsrMetadata metadata;
    private readonly HsrBinary binary;
    private readonly HsrUsages usages;

    private HsrMorax(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status, GamePlugin plugin)
        : base(image, plugin)
    {
        // The enum sharing flag is not read for this build, so enum sharing stays disabled.
        var header = DiscoverRegistration(targets => IsRegistration(targets, global.Length), readEnumSharing: false, executableOnly: false);
        binary = new HsrBinary(this, header, status);
        metadata = new HsrMetadata(global, startup, binary.Header, this, status);
        usages = new HsrUsages(metadata, binary, status);
    }

    internal static Il2CppInspector Load(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status, GamePlugin plugin)
    {
        var adapter = new HsrMorax(image, global, startup, status, plugin);
        var binary = adapter.binary.Load(adapter.metadata, adapter.usages);
        return new Il2CppInspector(binary, adapter.metadata.Model);
    }

    internal void AttachTables(
        Il2CppBinary binary,
        Metadata model,
        ImmutableArray<Il2CppType> types,
        Dictionary<ulong, int> typeAddresses,
        ImmutableArray<Il2CppGenericInst> instances,
        ImmutableArray<Il2CppMethodSpec> specs,
        ImmutableArray<uint> fieldOffsets,
        ImmutableArray<Il2CppTypeDefinitionSizes> sizes
    ) => Attach(binary, model, types, typeAddresses, instances, specs, fieldOffsets, sizes);

    private bool IsRegistration(ulong[] targets, int metadataLength)
    {
        if (
            !Image.TryMapVATR(targets[5] + 0x1FC, out _)
            || !Image.TryMapVATR(targets[2] + 0x80, out _)
            || !Image.TryMapVATR(targets[1] + 0x40, out _)
        )
            return false;
        var span = Image.ReadMappedUInt32(targets[5] + 0x1F8) ^ 0x1608C2C8u;
        if (span <= 1_000_000 || span > metadataLength)
            return false;
        var types = Image.ReadMappedUInt64(targets[2] + 0x80);
        var methods = Image.ReadMappedUInt64(targets[1] + 0x40);
        return types != 0 && methods != 0 && Image.TryMapVATR(types, out _) && Image.TryMapVATR(methods, out _);
    }

    public override List<MetadataUsage> Usages => usages.Usages;
    public override IEnumerable<AdditionalMetadataUsage> AdditionalUsages => usages.AdditionalUsages;
    public override int VTableSlotSize => 16;

    public override Il2CppTypeDefinitionSizes LayoutSizes(int group) => binary.LayoutSizes(group);

    public override uint LayoutFieldOffset(int group, int local) => binary.LayoutFieldOffset(group, local);

    public override int FieldStorageTag(Reflection.FieldInfo field) => binary.FieldStorageTag(field);

    public override string StorageBase(uint tag) => tag == 0 ? "klass->static_fields" : $"MORAX storage region {tag}";

    internal override int DefinitionLayoutGroup(int definition) => binary.DefinitionLayoutGroup(definition);

    internal override int LayoutAlignment(int group) => binary.LayoutAlignment(group);

    public override Reflection.TypeInfo ResolveGenericType(Reflection.TypeModel model, Il2CppType type) => binary.ResolveGenericType(model, type);

    public override (ulong Address, object Value) DecodeDefault(int typeIndex, int dataIndex, Il2CppBinary binary) =>
        HsrDefaults.Decode(metadata, typeIndex, dataIndex, binary);
}
