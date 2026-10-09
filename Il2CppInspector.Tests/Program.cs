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
                        + "--pdb-fixture <output directory> [llvm-pdbutil] [idat], --dwarf-fixture <output directory> [readelf] [gdb] [idat] [llvm-symbolizer], "
                        + "--tot-native <Native directory> <output directory>, --genshin <Native directory> [output directory] [options], "
                        + "--dwarf-globals <Android sample directory> <output directory> [idat], "
                        + "--debug-routing, --cli-targets <CLI DLL>, --progress [binary metadata output directory], or --stock <Unity player directory>."
                );
            }

            switch (args[0])
            {
                case "--genshin":
                    RequireArguments(args, 2);
                    Plugin.Genshin.GenshinTests.Run(args);
                    break;
                case "--bh3":
                    RequireArguments(args, 2);
                    Plugin.BH3.Bh3Tests.Run(args);
                    break;
                case "--cpp-layout":
                    Common.Cpp.CppTypeTests.Run(args.Length > 1 ? args[1] : null);
                    break;
                case "--progress":
                    ProgressBarTests.Run(args);
                    break;
                case "--debug-routing":
                    Common.Outputs.DebugOutputTests.Run();
                    break;
                case "--tot-native":
                    RequireArguments(args, 3);
                    Plugin.TOT.TotNativeOutputTests.Run(args[1], args[2]);
                    break;
                case "--dwarf-fixture":
                    RequireArguments(args, 2);
                    Common.Outputs.DwarfOutputTests.Run(
                        args[1],
                        args.Length > 2 ? args[2] : null,
                        args.Length > 3 ? args[3] : null,
                        args.Length > 4 ? args[4] : null,
                        args.Length > 5 ? args[5] : null
                    );
                    break;
                case "--dwarf-globals":
                    RequireArguments(args, 3);
                    Common.Outputs.DwarfGlobalTests.Run(args[1], args[2], args.Length > 3 ? args[3] : null);
                    break;
                case "--pdb-fixture":
                    RequireArguments(args, 2);
                    Common.Outputs.PdbOutputTests.Run(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);
                    break;
                case "--cli-targets":
                    RequireArguments(args, 2);
                    OutputTargetTests.Run(args[1]);
                    break;
                case "--plugins":
                    ZzzPluginTests.Run();
                    Plugin.Endfield.EndfieldPluginTests.Run();
                    break;
                case "--tot":
                    RequireArguments(args, 2);
                    Plugin.TOT.TotSelfTest.Run(args[1]);
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
