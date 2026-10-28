using Il2CppInspector.Tests.CLI;

namespace Il2CppInspector.Tests
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            switch (args.FirstOrDefault())
            {
                case "--plugins":
                    Common.Plugins.GamePluginsTests.Run();
                    break;
                case "--cpp-layout":
                    Common.Cpp.CppTypeTests.Run(Optional(args, 1));
                    break;
                case "--progress":
                    ProgressBarTests.Run(args);
                    break;
                case "--cli-targets":
                    OutputTargetTests.Run(args[1]);
                    break;
                case "--plugin-cli":
                    PluginCliTests.Run(args);
                    break;
                case "--stock":
                    Common.StockTests.Run(args);
                    break;
                case "--pdb-fixture":
                    Common.Outputs.PdbOutputTests.Run(args[1], Optional(args, 2), Optional(args, 3));
                    break;
                case "--dwarf-fixture":
                    Common.Outputs.DwarfOutputTests.Run(args[1], Optional(args, 2), Optional(args, 3), Optional(args, 4), Optional(args, 5));
                    break;
                case "--dwarf-globals":
                    Common.Outputs.DwarfGlobalTests.Run(args[1], args[2], Optional(args, 3));
                    break;
                case "--game":
                    Plugin.GameSampleTests.Run(args[1], args[2], Optional(args, 3));
                    break;
                default:
                    throw new ArgumentException(
                        "Usage: dotnet run --project Il2CppInspector.Tests -c Release -- <mode> [arguments]; modes are listed in README.md."
                    );
            }
        }

        private static string Optional(string[] args, int index) => index < args.Length ? args[index] : null;
    }
}
