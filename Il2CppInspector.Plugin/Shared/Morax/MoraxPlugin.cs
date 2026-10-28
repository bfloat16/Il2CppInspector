using System.Text.RegularExpressions;
using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.Morax;

/// <summary>Plugin entry shared by the MORAX games: PE x64 binary, embedded header and startup-metadata.dat.</summary>
public abstract class MoraxPlugin : GamePlugin
{
    public override string StartupMetadataFileName => "startup-metadata.dat";
    public override bool StreamExports => true;
    public override UnityVersion DefaultUnityVersion => new("2019.4.40f1");
    public override string RegistrationSignature => "void* (*Morax_MetadataCache_Register)()";
    public override string PythonMetadataProcessor =>
        ResourceHelper.GetText(typeof(MoraxPlugin).Assembly, "Il2CppInspector.Plugin.Morax.RuntimeCaches.py");

    // Every MORAX game uses the same container magic; select the build with --game.
    public override bool Matches(ReadOnlySpan<byte> metadata) => MoraxReader.IsMhy(metadata);

    public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
    {
        if (startupMetadata == null)
            throw new FileNotFoundException(
                $"{GameId} requires startup-metadata.dat. Supply --startup-metadata or place it beside global-metadata.dat."
            );
        var image = PEReader.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");
        return Load(image, metadata, startupMetadata, status);
    }

    private protected abstract Il2CppInspector Load(IFileFormatStream image, byte[] global, byte[] startup, EventHandler<string> status);

    /// <summary>Physical runtime records of this build. A null body leaves the stock record opaque.</summary>
    private protected abstract (string Name, string Body)[] RuntimeRecords { get; }

    public override UnityHeaders GetHeaders() => UnityHeaders.ForPlugin(new("2019.4.24"), DefaultUnityVersion, ApplyRuntimeRecords);

    private string ApplyRuntimeRecords(string stock)
    {
        foreach (var (name, body) in RuntimeRecords)
        {
            var pattern = $@"typedef struct (?:__attribute__\(\(aligned\([0-9]+\)\)\)\s+)?{name}\s*\{{.*?\}}\s*{name};";
            if (!Regex.IsMatch(stock, pattern, RegexOptions.Singleline))
                throw new InvalidOperationException($"Missing runtime header record {name}.");
            // Keep emitted records independent of source-file line endings.
            var replacement =
                body == null ? $"typedef struct {name} {name};" : $"typedef struct {name}\n{{\n{body.ReplaceLineEndings("\n")}\n}} {name};";
            stock = Regex.Replace(stock, pattern, _ => replacement, RegexOptions.Singleline);
        }
        return $"// {GameId} MORAX runtime layout. Encoded runtime members require decoding.\n" + stock;
    }

    public override IGameTypeLayouts CreateTypeLayouts(TypeModel model)
    {
        var adapter = (MoraxAdapter)model.Package.Metadata.GameAdapter;
        return new SharedTypeLayouts(model, adapter.GenericLayoutGroups, adapter.EnableEnumSharing);
    }

    public override IGameAnalysisModel CreateAnalysisModel(AppModel model)
    {
        ((NativeTypeModel)model.GameNativeModel).InitializeNaming(model);
        model.BuildMetadataDataUsages();
        return new NativeLayoutAnalysisModel(model);
    }
}
