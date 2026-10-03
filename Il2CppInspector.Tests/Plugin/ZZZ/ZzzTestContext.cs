namespace Il2CppInspector.Tests.Plugin.ZZZ
{
    internal sealed class ZzzTestContext
    {
        private TypeModel model;

        internal ZzzTestContext(string[] args)
        {
            var game = args[0];
            Input = Inspector
                .LoadFromFile(Path.Combine(game, "GameAssembly.dll"), Path.Combine(game, "global-metadata.dat"), args.Contains("--explicit-game") ? new LoadOptions { Game = "ZZZ_CN_3.2.0" } : null)
                ?.Single();
        }

        internal Inspector Input { get; }
        internal TypeModel Model => model ??= new TypeModel(Input);
        internal TypeInfo Vector => Model.TypesByFullName["UnityEngine.Vector3"];
        internal TypeInfo List => Model.TypesByFullName["System.Collections.Generic.List`1"];
        internal TypeInfo ObjectType => Model.TypesByFullName["System.Object"];
        internal TypeInfo NullablePolicy => Model.TypesByFullName["System.Nullable`1"].MakeGenericType(Model.TypesByFullName["Mono.Security.Interface.MonoSslPolicyErrors"]);
        internal TypeInfo Blend => Model.TypesByDefinitionIndex[37196].MakeGenericType(Model.TypesByFullName["PipelineCamera.WorldBasicCameraData"]);
        internal TypeInfo CameraTuple =>
            Model.TypesByDefinitionIndex[119].MakeGenericType(Model.TypesByDefinitionIndex[37197].MakeGenericType(Model.TypesByFullName["PipelineCamera.WorldBasicCameraData"]), Blend);
        internal uint[] RawOffsets =>
            (uint[])
                Input
                    .Metadata.GameAdapter.GetType()
                    .GetProperty("RawFieldOffsets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    .GetValue(Input.Metadata.GameAdapter);

        internal MethodBase GenericMethod
        {
            get
            {
                var spec = Input.GenericMethodPointers.Keys.First();
                return Model.GetMetadataUsageMethod(new MetadataUsage(MetadataUsageType.MethodRef, Input.MethodSpecs.IndexOf(spec)));
            }
        }
    }
}
