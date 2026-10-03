using System.Text;
using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Outputs;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.ZZZ
{
    public sealed class ZzzPlugin : GamePlugin
    {
        public override string GameId => "ZZZ_CN_3.2.0";
        public override string StartupMetadataFileName => "startup-metadata.dat";
        public override bool StreamExports => true;
        public override UnityVersion DefaultUnityVersion => new("2019.4.40f1");
        public override string RegistrationSignature => "void* (*Morax_MetadataCache_Register)()";
        public override string PythonMetadataProcessor => ResourceHelper.GetText(typeof(ZzzPlugin).Assembly, "Il2CppInspector.Plugin.ZZZ.RuntimeCaches.py");

        public override bool Matches(ReadOnlySpan<byte> metadata) => ZzzMetadataDetector.IsMorax(metadata);

        public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
        {
            if (startupMetadata == null)
            {
                throw new FileNotFoundException($"{GameId} requires startup-metadata.dat. Supply --startup-metadata or LoadOptions.StartupMetadataPath.");
            }

            var image = PEReader.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");
            return ZzzMorax.Load(image, metadata, startupMetadata, status, this);
        }

        public override UnityHeaders GetHeaders() => UnityHeaders.ForPlugin(new("2019.4.24"), DefaultUnityVersion, ZzzRuntimeHeaders.Apply);

        public override IGameTypeLayouts CreateTypeLayouts(TypeModel model) => new ZzzSharedTypeLayouts(model);

        public override GameNativeModel CreateNativeModel(TypeModel model) => new ZzzNativeModel(model);

        public override IGameAnalysisModel CreateAnalysisModel(AppModel model)
        {
            foreach (var usage in model.Package.MetadataUsages)
            {
                if (usage.Type == MetadataUsageType.StringLiteral)
                {
                    model.Strings.Add(usage.VirtualAddress, model.Package.StringLiterals[usage.SourceIndex]);
                }
                else if (usage.Type == MetadataUsageType.FieldInfo)
                {
                    var reference = model.Package.FieldRefs[usage.SourceIndex];
                    var fieldType = model.TypeModel.GetMetadataUsageType(usage);
                    var definition = fieldType.Definition.IsValid ? fieldType.Definition : fieldType.GetGenericTypeDefinition().Definition;
                    var field = fieldType.DeclaredFields.Single(f => f.Index == definition.FieldIndex + reference.FieldIndex);
                    var size = model.Package.Metadata.GameAdapter.FieldRvaSize(model.Package.TypeReferences[model.Package.Fields[field.Index].TypeIndex], model.Package.Binary);
                    var value =
                        field.HasFieldRVA && size > 0 && field.DefaultValueMetadataAddress != 0
                            ? Convert.ToHexString(model.Package.Metadata.ReadBytes((long)field.DefaultValueMetadataAddress, size))
                            : "";
                    model.Fields.Add(usage.VirtualAddress, (field, value));
                }
            }
            return new ZzzAnalysisModel(model);
        }

        public override void WriteAssemblies(TypeModel model, string path, bool suppressMetadata, EventHandler<string> status) =>
            new ZzzAssemblyWriter(model.Package, suppressMetadata, model).Write(path, status);

        public override void WriteHeader(AppModel model, string path, bool betterArraySize) => new ZzzHeaderWriter(model, betterArraySize).Write(path);

        public override void WriteJson(AppModel model, string path, bool allowComments) => new ZzzJsonMetadata(model, allowComments).Write(path);

        public override void WriteApplicationPointers(AppModel model, string path)
        {
            Directory.CreateDirectory(path);
            using (var functions = new StreamWriter(Path.Combine(path, "il2cpp-functions.h"), false, Encoding.UTF8))
            {
                functions.WriteLine("// ZZZ method and MethodInfo pointers; addresses are RVAs from the PE image base.");
                new ZzzJsonMetadata(model).WriteCppFunctions(functions);
            }
            using var types = new StreamWriter(Path.Combine(path, "il2cpp-types-ptr.h"), false, Encoding.UTF8);
            types.WriteLine("// ZZZ TypeInfo pointers; include inside the app namespace, as in the stock scaffolding.");
            var seen = new HashSet<string>();
            foreach (var usage in model.Package.MetadataUsages.Where(u => u.Type == MetadataUsageType.TypeInfo))
            {
                var klass = model.GameNativeModel.ClassName(model.TypeModel.TypesByReferenceIndex[usage.SourceIndex]);
                var name = klass == "Il2CppClass" ? $"Zzz_ClassRef_{usage.SourceIndex}" : klass[..^"__Class".Length];
                if (!seen.Add(name))
                {
                    continue;
                }

                if (klass == "Il2CppClass")
                {
                    types.WriteLine($"#ifndef {name}_DEFINED\n#define {name}_DEFINED\nusing {name}__Class = ::Il2CppClass;\n#endif");
                }

                types.WriteLine($"DO_TYPEDEF(0x{usage.VirtualAddress - model.Package.BinaryImage.ImageBase:X8}, {name});");
            }
        }
    }
}
