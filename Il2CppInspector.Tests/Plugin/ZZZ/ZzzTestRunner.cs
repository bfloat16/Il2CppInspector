namespace Il2CppInspector.Tests.Plugin.ZZZ
{
    internal static class ZzzTestRunner
    {
        internal static void Run(string[] args)
        {
            var context = new ZzzTestContext(args);
            IL2CPP.ZenlessZoneZero.ZzzBinaryTests.Run(context);
            Reflection.ZzzReflectionTests.Run(context);

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
