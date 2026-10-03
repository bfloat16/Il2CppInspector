using VersionedSerialization;

namespace Il2CppInspector
{
    public partial class Metadata
    {
        public Plugins.GameMetadataAdapter GameAdapter { get; private set; }
        public Plugins.GamePlugin GamePlugin => GameAdapter?.Plugin;
        public bool HasGameAdapter => GameAdapter != null;
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
            metadata.Strings.Add(0, "");
            metadata.Strings.Add(-1, "");
            return metadata;
        }
    }
}
