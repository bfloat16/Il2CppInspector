namespace Il2CppInspector.Tests.Plugin.ZZZ.IL2CPP.ZenlessZoneZero
{
    internal static class ZzzBinaryTests
    {
        internal static void Run(ZzzTestContext context)
        {
            var input = context.Input;
            Check(input?.Metadata.IsZenlessZoneZero == true, "MORAX automatically selects the C# ZZZ loader");
            Check(input.Metadata.GamePlugin.GameId == "ZZZ_CN_3.2.0", "The loaded package retains its selected game plugin");
            var exports = input.BinaryImage.GetExports().ToDictionary(e => e.Name, e => e.VirtualAddress);
            Check(
                exports["DllCanUnloadNow"] == 0x1802779A0 && exports["DllGetActivationFactory"] == 0x18026D900 && exports["il2cpp_get_api_table"] == 0x1802B7040,
                "PE export RVAs match the three IDA entrypoints without the raw-file displacement"
            );
            Check(input.TypeDefinitions.Length == 95437 && input.Images.Length == 170, "95,437 real definitions and 170 images exclude the two trailing sentinels");
            Check(
                input.Metadata.InterfaceUsageIndices.Length == 49353 && input.TypeDefinitions.All(t => t.ByValTypeIndex >= 0 && (t.Token & 0xFFFFFF) != 0),
                "No ghost types or interfaces from sentinel records"
            );
            Check(
                input.Assemblies.Count(a => (uint)a.Aname.HashAlg == 0x8004) == 169 && input.Assemblies.Count(a => (uint)a.Aname.HashAlg == 0) == 1,
                "All assembly hashes decode to SHA1 or None without the extra addition"
            );
            Check(
                input.TypeDefinitions[1568].Bitfield.PackingSize == PackingSize.One
                    && input.TypeDefinitions[1568].Bitfield.ClassSize == PackingSize.One
                    && input.TypeDefinitions[5965].Bitfield.PackingSize == PackingSize.SixtyFour
                    && input.TypeDefinitions[0].Bitfield.DefaultPackingSize
                    && input.TypeDefinitions[0].Bitfield.DefaultClassSize,
                "Full metadata bitfields retain packing and default-size flags"
            );
            Check(
                input
                    .Metadata.FieldDefaultValues.Where(v => input.TypeReferences[v.TypeIndex].Type == Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
                    .All(v => input.FieldDefaultValue[v.FieldIndex].Item2 == null && input.FieldDefaultValue[v.FieldIndex].Item1 != 0),
                "Structure defaults retain blob addresses without fabricated integer constants"
            );
            Check(input.Methods.Length == 829363 && input.Fields.Length == 485233, "829,363 methods and 485,233 fields");
            Check(input.Properties.Length == 131887 && input.Properties.Skip(95857).All(p => !input.Strings[p.NameIndex].Contains('\uFFFD')), "Property names above the 32-bit overflow boundary");
            Check(input.TypeReferences.Length == 939421 && input.StringLiterals.Length == 85294, "939,421 type references and 85,294 literals");
            Check(input.Metadata.ParameterDefaultValues.Length == 30900 && input.FieldRefs.Length == 1020, "30,900 parameter defaults and 1,020 FieldRefs");
            Check(input.TypeDefinitions.Count(t => t.DeclaringTypeIndex >= 0) == 28346, "28,346 nested types use the recovered parent map");
            Check(input.MetadataUsages.All(u => u.Type != MetadataUsageType.FieldRva), "MORAX kind 7 is not interpreted as stock FieldRva");
            Check(input.GenericMethodPointers.Count > 800000, "Generic methods retain compiled addresses");
        }
    }
}
