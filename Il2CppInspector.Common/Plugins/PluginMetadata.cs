using VersionedSerialization;

namespace Il2CppInspector
{
    public partial class Metadata
    {
        public Plugins.GameMetadataAdapter GameAdapter { get; private set; }
        public Plugins.GamePlugin GamePlugin => GameAdapter?.Plugin;

        /// <summary>
        /// When true, the adapter is kept for its decoded tables but every HasGameAdapter-gated stock
        /// path (analysis model, assembly/header/JSON writers) runs its normal implementation instead of
        /// delegating back into the plugin. A plugin whose decoded metadata is stock-shaped sets this
        /// after construction so the standard output pipeline can be reused.
        /// </summary>
        public bool SuppressGameAdapter { get; set; }

        public bool HasGameAdapter => GameAdapter != null && !SuppressGameAdapter;
        public bool IsZenlessZoneZero => GamePlugin?.GameId.StartsWith("ZZZ_", StringComparison.OrdinalIgnoreCase) == true;

        public static Metadata CreateForPlugin(byte[] data, Plugins.GameMetadataAdapter adapter, StructVersion version, EventHandler<string> status = null)
        {
            ArgumentNullException.ThrowIfNull(adapter);
            var metadata = new Metadata(status)
            {
                Version = version,
                Header = new(),
                GameAdapter = adapter,
                IsModified = true,
            };
            metadata.Capacity = data.Length;
            metadata.Write(data);
            metadata.Position = 0;
            // Only -1 is a synthetic sentinel (the "no string" index used throughout the metadata
            // tables). String index 0 is a real string-table offset, so it must not be pre-seeded:
            // doing so masked the first string of a plugin's table and made its intern pass skip the
            // real value. In TOT's table index 0 is "mscorlib" (the mscorlib assembly name); losing it
            // produced a DummyDll whose mscorlib assembly had an empty name, which made dnlib fail to
            // recognise System.ValueType as a corlib type and abort on the __StaticArrayInitTypeSize
            // field RVAs ("initial value size != size of field type").
            metadata.Strings.Add(-1, "");
            return metadata;
        }
    }
}
