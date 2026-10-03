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
        public abstract string RegistrationSignature { get; }

        public abstract bool Matches(ReadOnlySpan<byte> metadata);
        public abstract Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status);
        public abstract UnityHeaders GetHeaders();
        public abstract IGameTypeLayouts CreateTypeLayouts(TypeModel model);
        public abstract GameNativeModel CreateNativeModel(TypeModel model);
        public abstract IGameAnalysisModel CreateAnalysisModel(AppModel model);
        public abstract void WriteAssemblies(TypeModel model, string path, bool suppressMetadata, EventHandler<string> status);
        public abstract void WriteHeader(AppModel model, string path, bool betterArraySize);
        public abstract void WriteJson(AppModel model, string path, bool allowComments);
        public abstract void WriteApplicationPointers(AppModel model, string path);
    }
}
