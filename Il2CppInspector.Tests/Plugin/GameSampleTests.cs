using Cecil = Mono.Cecil;

namespace Il2CppInspector.Tests.Plugin;

// Every game plugin runs the same checks against its local sample directory.
internal static class GameSampleTests
{
    // Decoded table sizes and runtime layouts measured on the shipped samples. A wrong key, record
    // permutation or layout hook changes at least one value. A null baseline prints the measured values.
    private static readonly Dictionary<string, (string Binary, string Baseline)> Samples = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BH3_CN_Windows_9.1.0"] = (
            "UserAssembly.dll",
            "types=77799 methods=561798 fields=362445 params=460434 literals=104856 typeRefs=469900 methodSpecs=251755 usages=348209 attributes=41122 Il2CppClass=0xD8 Il2CppClass.static_fields=0x30 Il2CppClass.vtable_count=0xCE MethodInfo=0x40 MethodInfo.flags=0x30 Il2CppCodeRegistration=0x98 Il2CppMetadataRegistration=0xA8"
        ),
        ["Endfield_CN_Android_x.x.x"] = ("libil2cpp.so", null),
        ["Endfield_CN_Windows_1.6.3"] = (
            "GameAssembly.dll",
            "types=91478 methods=574771 fields=323675 params=789641 literals=52412 typeRefs=251351 methodSpecs=665889 usages=312697 attributes=0 Il2CppClass=0x338 Il2CppClass.static_fields=0xB8 Il2CppClass.vtable_count=0x126 MethodInfo=0x58 MethodInfo.flags=0x4C Il2CppCodeRegistration=0x78 Il2CppMetadataRegistration=0x80"
        ),
        ["Genshin_CN_Windows_7.1.0"] = (
            "YuanShen.exe",
            "types=88902 methods=733442 fields=440172 params=548400 literals=86108 typeRefs=683568 methodSpecs=570843 usages=378720 attributes=74864 Il2CppClass=0xD8 Il2CppClass.static_fields=0x78 Il2CppClass.vtable_count=0xBE MethodInfo=0x38 MethodInfo.flags=0x28 Il2CppCodeRegistration=0xA8 Il2CppMetadataRegistration=0x108"
        ),
        ["HSR_CN_Windows_4.6.51"] = ("GameAssembly.dll", null),
        ["HSR_OS_Windows_4.6.0"] = ("GameAssembly.dll", null),
        ["LAD_CN_Android_6.0.0"] = (
            "libil2cpp.so",
            "types=45515 methods=314124 fields=179776 params=402106 literals=62223 typeRefs=125923 methodSpecs=112354 usages=225701 attributes=0 Il2CppClass=0x320 Il2CppClass.static_fields=0xB0 Il2CppClass.vtable_count=0x10E MethodInfo=0x58 MethodInfo.flags=0x4C Il2CppCodeRegistration=0x78 Il2CppMetadataRegistration=0x80"
        ),
        ["TOT_CN_Android_6.1.0"] = (
            "libil2cpp.so",
            "types=29323 methods=227533 fields=228394 params=212861 literals=36670 typeRefs=219071 methodSpecs=155450 usages=169795 attributes=67986 Il2CppClass=0x328 Il2CppClass.static_fields=0xB8 Il2CppClass.vtable_count=0x11A MethodInfo=0x50 MethodInfo.flags=0x44 Il2CppCodeRegistration=0x80 Il2CppMetadataRegistration=0x80"
        ),
        ["TOT_OS_Android_5.8.1"] = (
            "libil2cpp.so",
            "types=29548 methods=228587 fields=228551 params=213683 literals=36922 typeRefs=219790 methodSpecs=156032 usages=170539 attributes=68312 Il2CppClass=0x328 Il2CppClass.static_fields=0xB8 Il2CppClass.vtable_count=0x11A MethodInfo=0x50 MethodInfo.flags=0x44 Il2CppCodeRegistration=0x80 Il2CppMetadataRegistration=0x80"
        ),
        ["ZZZ_CN_Windows_3.2.0"] = (
            "GameAssembly.dll",
            "types=95437 methods=829363 fields=485233 params=706463 literals=85294 typeRefs=939421 methodSpecs=864093 usages=529737 attributes=90441 Il2CppClass=0xD8 Il2CppClass.static_fields=0x20 Il2CppClass.vtable_count=0xC4 MethodInfo=0x40 MethodInfo.flags=0x2C Il2CppCodeRegistration=0x90 Il2CppMetadataRegistration=0x98"
        ),
        ["ZZZ_CN_Windows_3.3.0"] = (
            "GameAssembly.dll",
            "types=96786 methods=854677 fields=497653 params=727351 literals=87661 typeRefs=970056 methodSpecs=894410 usages=547655 attributes=93854 Il2CppClass=0xD8 Il2CppClass.static_fields=0x40 Il2CppClass.vtable_count=0xC6 MethodInfo=0x40 MethodInfo.flags=0x2E Il2CppCodeRegistration=0x88 Il2CppMetadataRegistration=0x98"
        ),
    };

    private static readonly string[] LayoutMembers =
    [
        "Il2CppClass",
        "Il2CppClass.static_fields",
        "Il2CppClass.vtable_count",
        "MethodInfo",
        "MethodInfo.flags",
        "Il2CppCodeRegistration",
        "Il2CppMetadataRegistration",
    ];

    internal static void RunInstalled()
    {
        foreach (var plugin in GamePlugins.Installed)
            Check(Samples.ContainsKey(plugin.GameId), "Installed plugin has a sample entry: " + plugin.GameId);
    }

    internal static void Run(string gameId, string directory, string output)
    {
        var (binary, baseline) = Samples[gameId];
        var plugin = GamePlugins.Get(gameId);
        var metadataPath = Path.Combine(directory, "global-metadata.dat");
        Check(plugin.Matches(File.ReadAllBytes(metadataPath)), $"{gameId} matches its sample metadata");

        var package = Inspector.LoadFromFile(Path.Combine(directory, binary), metadataPath, new LoadOptions { Game = gameId }).Single();
        var model = new TypeModel(package);
        var app = new AppModel(model, false).Build(plugin.DefaultUnityVersion);
        var measured = string.Join(
            " ",
            new[]
            {
                $"types={package.TypeDefinitions.Length}",
                $"methods={package.Methods.Length}",
                $"fields={package.Fields.Length}",
                $"params={package.Params.Length}",
                $"literals={package.StringLiterals.Length}",
                $"typeRefs={package.TypeReferences.Length}",
                $"methodSpecs={package.MethodSpecs.Length}",
                $"usages={package.MetadataUsages.Count}",
                $"attributes={model.AttributesByIndices.Count}",
            }.Concat(LayoutMembers.Select(m => $"{m}={Layout(app.RuntimeCppTypes, m)}"))
        );
        if (baseline == null)
            throw new InvalidOperationException($"{gameId} has no baseline. Measured: {measured}");
        var mismatches = measured.Split(' ').Except(baseline.Split(' '));
        Check(!mismatches.Any(), $"{gameId} decoded tables and runtime layouts match the baseline {string.Join(" ", mismatches)}".TrimEnd());

        if (output == null)
            return;
        var dlls = Path.Combine(output, "DummyDll");
        new AssemblyShims(model).Write(dlls);
        VerifyAssemblyReferences(dlls);
    }

    // Games without a runtime header override keep the stock declarations, so a member may be absent.
    private static string Layout(CppTypeCollection headers, string member)
    {
        var parts = member.Split('.');
        var type = headers.GetComplexType(parts[0]);
        var value = parts.Length == 1 ? type?.SizeBytes : type?.Flattened[parts[1]]?.OffsetBytes;
        return value is { } v ? $"0x{v:X}" : "none";
    }

    // Mono.Cecil 0.11.6 matches AnimeStudio, which loads DummyDll without search directories.
    private static void VerifyAssemblyReferences(string directory)
    {
        using var resolver = new RegisteredAssemblyResolver();
        var parameters = new Cecil.ReaderParameters { AssemblyResolver = resolver };
        var assemblies = Directory.GetFiles(directory, "*.dll").Select(f => Cecil.AssemblyDefinition.ReadAssembly(f, parameters)).ToList();
        foreach (var assembly in assemblies)
            resolver.Register(assembly);
        var unresolved = assemblies
            .SelectMany(a => a.MainModule.AssemblyReferences)
            .Where(r => !assemblies.Any(a => ReferenceEquals(resolver.Resolve(r), a) && a.Name.FullName == r.FullName))
            .Select(r => r.FullName)
            .Distinct()
            .ToArray();
        Check(unresolved.Length == 0, $"DummyDll references resolve to the written assemblies {string.Join(", ", unresolved)}".TrimEnd());
    }

    private sealed class RegisteredAssemblyResolver : Cecil.DefaultAssemblyResolver
    {
        internal void Register(Cecil.AssemblyDefinition assembly) => RegisterAssembly(assembly);
    }
}
