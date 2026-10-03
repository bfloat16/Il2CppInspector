using Il2CppInspector.Tests.CLI;

namespace Il2CppInspector.Tests
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                throw new ArgumentException(
                    "Usage: dotnet run --project Il2CppInspector.Tests -c Release -- "
                        + "<ZZZ Native directory> [output directory] [options], --plugins, "
                        + "--plugin-cli <CLI DLL> <isolated directory>, --assembly-resolution <ZZZ DummyDll directory>, "
                        + "or --stock <Unity player directory>."
                );
            }

            switch (args[0])
            {
                case "--plugins":
                    ZzzPluginTests.Run();
                    break;
                case "--plugin-cli":
                    RequireArguments(args, 3);
                    PluginCliTests.Run(args);
                    break;
                case "--stock":
                    RequireArguments(args, 2);
                    Common.StockTests.Run(args);
                    break;
                case "--assembly-resolution":
                    RequireArguments(args, 2);
                    Plugin.ZZZ.Outputs.ZzzAssemblyWriterTests.VerifyAssemblyResolution(args[1]);
                    break;
                default:
                    ZzzTestRunner.Run(args);
                    break;
            }
        }

        private static void RequireArguments(string[] args, int count)
        {
            if (args.Length < count)
            {
                throw new ArgumentException($"{args[0]} requires {count - 1} argument(s).");
            }
        }
    }
}
