using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugins
{
    public abstract class GamePlugin
    {
        public abstract string GameId { get; }
        public virtual string StartupMetadataFileName => null;
        public virtual bool StreamExports => false;
        public virtual UnityVersion DefaultUnityVersion => null;
        public virtual string PythonMetadataProcessor => "";
        public virtual string RegistrationSignature => null;

        public abstract bool Matches(ReadOnlySpan<byte> metadata);
        public abstract Il2CppInspector Load(
            Stream binary,
            byte[] metadata,
            byte[] startupMetadata,
            LoadOptions options,
            EventHandler<string> status
        );

        public virtual UnityHeaders GetHeaders() => throw new NotSupportedException($"{GameId} uses the stock IL2CPP pipeline.");

        public virtual IGameTypeLayouts CreateTypeLayouts(TypeModel model) =>
            throw new NotSupportedException($"{GameId} uses the stock IL2CPP pipeline.");

        public virtual GameNativeModel CreateNativeModel(TypeModel model) =>
            throw new NotSupportedException($"{GameId} uses the stock IL2CPP pipeline.");

        public virtual IGameAnalysisModel CreateAnalysisModel(AppModel model) =>
            throw new NotSupportedException($"{GameId} uses the stock IL2CPP pipeline.");
    }
}
