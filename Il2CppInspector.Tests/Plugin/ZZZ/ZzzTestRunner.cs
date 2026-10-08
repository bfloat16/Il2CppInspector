namespace Il2CppInspector.Tests.Plugin.ZZZ
{
    internal static class ZzzTestRunner
    {
        internal static void Run(string[] args)
        {
            var context = new ZzzTestContext(args);
            IL2CPP.ZenlessZoneZero.ZzzBinaryTests.Run(context);
            Reflection.ZzzReflectionTests.Run(context);

            if (args.Contains("--native-names"))
            {
                Model.ZzzNativeNamingTests.Run(context, args[1], args);
                return;
            }

            if (args.Contains("--debug-supplement"))
            {
                var app = new AppModel(context.Model, false).Build(new UnityVersion("2019.4.40f1"));
                Outputs.ZzzPythonScriptTests.VerifyDebugSupplement(app, args[1]);
                return;
            }

            if (args.Contains("--dwarf-only"))
            {
                Outputs.ZzzDwarfTests.Run(context, args[1]);
                return;
            }

            if (args.Contains("--native-profile"))
            {
                Outputs.ZzzPdbTests.Profile(context, args[1], args.Contains("--with-pdb"));
                return;
            }

            if (args.Contains("--pdb-only"))
            {
                Outputs.ZzzPdbTests.Run(context, args[1]);
                return;
            }

            if (args.Contains("--name-translation"))
            {
                Outputs.ZzzAssemblyWriterTests.VerifyNameTranslation(context, args);
                return;
            }

            if (args.Contains("--analysis-model") || args.Contains("--analysis-layouts"))
            {
                Model.ZzzAnalysisModelTests.Run(context, args);
                return;
            }

            if (args.Length > 1)
            {
                Outputs.ZzzOutputTests.Run(context, args);
            }
        }
    }
}
