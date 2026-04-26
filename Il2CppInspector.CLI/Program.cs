using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Outputs;
using Il2CppInspector.Reflection;
using Inspector = Il2CppInspector.Il2CppInspector;

namespace Il2CppInspector.CLI
{
    internal class Options
    {
        public string BinaryFile;
        public string ImageBase;
        public string MetadataFile;
        public string OutputDir = "output";
        public string ScriptTarget;
        public string UnityVersion;
    }

    internal static class NativeDialogs
    {
        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_NOCHANGEDIR = 0x00000008;

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;

        [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileNameW(ref OPENFILENAME lpofn);

        [SupportedOSPlatform("windows")]
        public static unsafe string ShowOpenFileDialog(string title, string filter)
        {
            char* buf = stackalloc char[260];
            buf[0] = '\0';

            OPENFILENAME ofn = new()
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                lpstrFilter = filter,
                lpstrFile = (nint)buf,
                nMaxFile = 260,
                lpstrTitle = title,
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
            };

            return GetOpenFileNameW(ref ofn) ? new string(buf) : null;
        }

        // IFileOpenDialog COM interfaces for modern folder picker
        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid rclsid, nint pUnkOuter, uint dwClsContext, ref Guid riid, out nint ppv);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        [DllImport("ole32.dll", EntryPoint = "CoTaskMemFree")]
        private static extern void CoTaskMemFree(nint pv);

        [SupportedOSPlatform("windows")]
        public static string ShowFolderDialog(string title)
        {
            string result = null;
            Thread thread = new(() =>
            {
                CoInitializeEx(
                    0,
                    2 /* COINIT_APARTMENTTHREADED */
                );
                try
                {
                    result = ShowFolderDialogImpl(title);
                }
                finally
                {
                    CoUninitialize();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }

        [SupportedOSPlatform("windows")]
        private static unsafe string ShowFolderDialogImpl(string title)
        {
            // CLSID_FileOpenDialog = {DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7}
            Guid clsid = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
            // IID_IFileOpenDialog = {D57C7288-D4AD-4768-BE02-9D969532D960}
            Guid iid = new("D57C7288-D4AD-4768-BE02-9D969532D960");

            int hr = CoCreateInstance(
                ref clsid,
                0,
                1 /* CLSCTX_INPROC_SERVER */
                ,
                ref iid,
                out nint pDialog
            );
            if (hr < 0)
            {
                return null;
            }

            // Get vtable pointer
            nint* vtable = *(nint**)pDialog;

            // IFileDialog::GetOptions (index 10)
            delegate* unmanaged[Stdcall]<nint, out uint, int> getOptions = (delegate* unmanaged[Stdcall]<nint, out uint, int>)vtable[10];
            getOptions(pDialog, out uint options);

            // IFileDialog::SetOptions (index 9)
            delegate* unmanaged[Stdcall]<nint, uint, int> setOptions = (delegate* unmanaged[Stdcall]<nint, uint, int>)vtable[9];
            setOptions(pDialog, options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);

            // IFileDialog::SetTitle (index 17)
            fixed (char* pTitle = title)
            {
                delegate* unmanaged[Stdcall]<nint, char*, int> setTitle = (delegate* unmanaged[Stdcall]<nint, char*, int>)vtable[17];
                setTitle(pDialog, pTitle);
            }

            // IFileDialog::Show (index 3)
            delegate* unmanaged[Stdcall]<nint, nint, int> show = (delegate* unmanaged[Stdcall]<nint, nint, int>)vtable[3];
            hr = show(pDialog, 0);

            string result = null;
            if (hr >= 0)
            {
                // IFileDialog::GetResult (index 20)
                delegate* unmanaged[Stdcall]<nint, out nint, int> getResult = (delegate* unmanaged[Stdcall]<nint, out nint, int>)vtable[20];
                hr = getResult(pDialog, out nint pItem);

                if (hr >= 0)
                {
                    nint* itemVtable = *(nint**)pItem;
                    // IShellItem::GetDisplayName (index 5)
                    delegate* unmanaged[Stdcall]<nint, uint, out nint, int> getDisplayName = (delegate* unmanaged[Stdcall]<nint, uint, out nint, int>)itemVtable[5];
                    hr = getDisplayName(
                        pItem,
                        0x80058000 /* SIGDN_FILESYSPATH */
                        ,
                        out nint pName
                    );

                    if (hr >= 0)
                    {
                        result = new string((char*)pName);
                        CoTaskMemFree(pName);
                    }

                    // IUnknown::Release
                    delegate* unmanaged[Stdcall]<nint, uint> releaseItem = (delegate* unmanaged[Stdcall]<nint, uint>)itemVtable[2];
                    releaseItem(pItem);
                }
            }

            // IUnknown::Release
            delegate* unmanaged[Stdcall]<nint, uint> release = (delegate* unmanaged[Stdcall]<nint, uint>)vtable[2];
            release(pDialog);

            return result;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENFILENAME
        {
            public int lStructSize;
            public nint hwndOwner;
            public nint hInstance;
            public string lpstrFilter;
            public nint lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public nint lpstrFile;
            public int nMaxFile;
            public nint lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public nint lCustData;
            public nint lpfnHook;
            public string lpTemplateName;
            public nint pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }
    }

    internal static class ProgressBar
    {
        private static int _lastLineLen;

        public static void Update(string message)
        {
            string line = $"\r  {message}";
            int pad = Math.Max(0, _lastLineLen - line.Length);
            Console.Write(line + new string(' ', pad));
            _lastLineLen = line.Length;
        }

        public static void Update(string label, int current, int total)
        {
            int pct = total > 0 ? current * 100 / total : 0;
            int barLen = 30;
            int filled = total > 0 ? current * barLen / total : 0;
            string bar = new string('#', filled) + new string('-', barLen - filled);
            Update($"{label} [{bar}] {pct}% ({current}/{total})");
        }

        public static void Done(string message = null)
        {
            if (message != null)
            {
                Update(message);
            }

            Console.WriteLine();
            _lastLineLen = 0;
        }

        public static void RunWithSpinner(string label, Action action)
        {
            char[] spinChars = ['|', '/', '-', '\\'];
            int spinIdx = 0;
            Stopwatch sw = Stopwatch.StartNew();
            bool done = false;

            Thread spinThread = new(() =>
            {
                while (!Volatile.Read(ref done))
                {
                    Update($"{label} {spinChars[spinIdx++ % spinChars.Length]} ({sw.Elapsed.TotalSeconds:F1}s)");
                    Thread.Sleep(100);
                }
            })
            {
                IsBackground = true,
            };
            spinThread.Start();

            try
            {
                action();
            }
            finally
            {
                Volatile.Write(ref done, true);
                spinThread.Join();
                Done($"{label} done ({sw.Elapsed.TotalSeconds:F1}s)");
            }
        }
    }

    internal static class Program
    {
        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            if (args.Length == 0)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    RunInteractive();
                }
                else
                {
                    PrintHelp();
                }

                return;
            }

            Options options = ParseArgs(args);
            if (options == null)
            {
                return;
            }

            Run(options);
        }

        private static Options ParseArgs(string[] args)
        {
            Options opts = new();

            for (int i = 0; i < args.Length; i++)
            {
                string value = null;

                bool NeedValue()
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine($"Option {args[i]} requires a value.");
                        return false;
                    }

                    value = args[++i];
                    return true;
                }

                switch (args[i])
                {
                    case "-i" or "--bin":
                        if (!NeedValue())
                        {
                            return null;
                        }

                        opts.BinaryFile = value;
                        break;
                    case "-m" or "--metadata":
                        if (!NeedValue())
                        {
                            return null;
                        }

                        opts.MetadataFile = value;
                        break;
                    case "-o" or "--output":
                        if (!NeedValue())
                        {
                            return null;
                        }

                        opts.OutputDir = value;
                        break;
                    case "-t" or "--script-target":
                        if (!NeedValue())
                        {
                            return null;
                        }

                        opts.ScriptTarget = value;
                        break;
                    case "--unity-version":
                        if (!NeedValue())
                        {
                            return null;
                        }

                        opts.UnityVersion = value;
                        break;
                    case "--image-base":
                        if (!NeedValue())
                        {
                            return null;
                        }

                        opts.ImageBase = value;
                        break;
                    case "-h" or "--help":
                        PrintHelp();
                        return null;
                    default:
                        Console.Error.WriteLine($"Unknown option: {args[i]}");
                        PrintHelp();
                        return null;
                }
            }

            if (string.IsNullOrEmpty(opts.BinaryFile) || string.IsNullOrEmpty(opts.MetadataFile))
            {
                Console.Error.WriteLine("Both --bin and --metadata are required.");
                PrintHelp();
                return null;
            }

            return opts;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(
                @"Il2CppInspector - IL2CPP binary analysis tool

Usage:
  Il2CppInspector [options]
  Il2CppInspector                      (Windows: open file dialogs)

Options:
  -i, --bin <file>          IL2CPP binary file (required)
  -m, --metadata <file>     global-metadata.dat file (required)
  -o, --output <dir>        Output directory (default: output)
  -t, --script-target <t>   Python script target: IDA, BinaryNinja, Ghidra
      --unity-version <v>   Unity version override (e.g. 2021.3.0f1)
      --image-base <hex>    Image base address for ELF memory dumps (hex)
  -h, --help                Show this help

Output structure:
  <output>/DummyDll/        .NET assembly shim DLLs
  <output>/CS/              C# type definitions (tree layout)
  <output>/il2cpp.py        Python script (if --script-target specified)
  <output>/il2cpp.h         C++ type header (if --script-target specified)
  <output>/il2cpp.json      JSON metadata (if --script-target specified)"
            );
        }

        [SupportedOSPlatform("windows")]
        private static void RunInteractive()
        {
            Console.WriteLine("Il2CppInspector - No arguments provided, opening file dialogs...");
            Console.WriteLine();

            string binary = NativeDialogs.ShowOpenFileDialog("Select IL2CPP binary", "IL2CPP Binary (*.so;*.dll)\0*.so;*.dll\0All files (*.*)\0*.*\0");

            if (string.IsNullOrEmpty(binary))
            {
                Console.Error.WriteLine("No binary file selected.");
                return;
            }

            Console.WriteLine($"Binary: {binary}");

            string metadata = NativeDialogs.ShowOpenFileDialog("Select global-metadata.dat", "Metadata (*.dat)\0*.dat\0All files (*.*)\0*.*\0");

            if (string.IsNullOrEmpty(metadata))
            {
                Console.Error.WriteLine("No metadata file selected.");
                return;
            }

            Console.WriteLine($"Metadata: {metadata}");

            string outputDir = NativeDialogs.ShowFolderDialog("Select output folder");

            if (string.IsNullOrEmpty(outputDir))
            {
                Console.Error.WriteLine("No output folder selected.");
                return;
            }

            Console.WriteLine($"Output: {outputDir}");
            Console.WriteLine();

            Run(
                new Options
                {
                    BinaryFile = binary,
                    MetadataFile = metadata,
                    OutputDir = outputDir,
                }
            );
        }

        private static void Run(Options options)
        {
            if (!File.Exists(options.BinaryFile))
            {
                Console.Error.WriteLine($"Binary file not found: {options.BinaryFile}");
                return;
            }

            if (!File.Exists(options.MetadataFile))
            {
                Console.Error.WriteLine($"Metadata file not found: {options.MetadataFile}");
                return;
            }

            if (options.ScriptTarget != null)
            {
                List<string> targets = PythonScript.GetAvailableTargets().ToList();
                if (!targets.Contains(options.ScriptTarget))
                {
                    Console.Error.WriteLine($"Unknown script target: {options.ScriptTarget}");
                    Console.Error.WriteLine($"Available targets: {string.Join(", ", targets)}");
                    return;
                }
            }

            LoadOptions loadOptions = new();

            if (!string.IsNullOrEmpty(options.ImageBase))
            {
                try
                {
                    loadOptions.ImageBase = Convert.ToUInt64(options.ImageBase, 16);
                }
                catch
                {
                    Console.Error.WriteLine($"Invalid image base address: {options.ImageBase}");
                    return;
                }
            }

            UnityVersion unityVersion = null;
            if (!string.IsNullOrEmpty(options.UnityVersion))
            {
                try
                {
                    unityVersion = new UnityVersion(options.UnityVersion);
                }
                catch
                {
                    Console.Error.WriteLine($"Invalid Unity version: {options.UnityVersion}");
                    return;
                }
            }

            Console.WriteLine("Loading IL2CPP data...");

            List<Inspector> il2cppList;
            il2cppList = Inspector.LoadFromFile(options.BinaryFile, options.MetadataFile, loadOptions, (_, msg) => Console.WriteLine($"  {msg}"));

            Console.WriteLine($"Loaded {il2cppList.Count} image(s).");
            Console.WriteLine();

            string outputBase = options.OutputDir;
            Directory.CreateDirectory(outputBase);

            for (int imageIndex = 0; imageIndex < il2cppList.Count; imageIndex++)
            {
                Inspector il2cpp = il2cppList[imageIndex];
                string suffix = il2cppList.Count > 1 ? $"-{imageIndex}" : "";
                string output = il2cppList.Count > 1 ? outputBase + suffix : outputBase;

                if (il2cppList.Count > 1)
                {
                    Console.WriteLine($"=== Processing image {imageIndex} ===");
                }

                // Type model with spinner
                TypeModel model = null;
                ProgressBar.RunWithSpinner("Building type model...", () => model = new TypeModel(il2cpp));

                // DummyDll with per-assembly progress
                string dllOut = Path.Combine(output, "DummyDll");
                Console.WriteLine($"Generating DummyDlls -> {dllOut}");
                int asmCount = model.Assemblies.Count;
                int dllStep = 0;
                new AssemblyShims(model).Write(
                    dllOut,
                    (_, msg) =>
                    {
                        dllStep++;
                        ProgressBar.Update("DummyDll", dllStep, asmCount * 3);
                    }
                );
                ProgressBar.Done();

                // C# stubs with spinner
                string csOut = Path.Combine(output, "CS");
                ProgressBar.RunWithSpinner($"Generating C# stubs -> {csOut}", () => new CSharpCodeStubs(model).WriteFilesByClassTree(csOut, false));

                // Python script with spinner
                if (options.ScriptTarget != null)
                {
                    AppModel appModel = null;
                    ProgressBar.RunWithSpinner("Building application model...", () => appModel = new AppModel(model, false).Build(unityVersion));

                    string pyOut = Path.Combine(output, "il2cpp.py");
                    ProgressBar.RunWithSpinner($"Generating {options.ScriptTarget} Python script -> {pyOut}", () => new PythonScript(appModel).WriteScriptToFile(pyOut, options.ScriptTarget));
                }

                Console.WriteLine();
            }

            Console.WriteLine("Done.");
        }
    }
}
