using System.Reflection.PortableExecutable;
using Il2CppInspector.Model;
using Il2CppInspector.Outputs.Pdb;

namespace Il2CppInspector.Outputs;

public sealed record PdbOutputResult(int Functions, int TypedFunctions, int TypeRecords, Guid Guid, uint Age, bool HasCodeView)
{
    public int Globals { get; init; }
}

public sealed class PdbOutput(AppModel model)
{
    public static bool Supports(IFileFormatStream image) => image.Format.StartsWith("PE", StringComparison.Ordinal) && image.Bits == 64 && image.Arch == "x64";

    public PdbOutputResult Write(string outputFile, EventHandler<string> status = null)
    {
        var image = model.Package.BinaryImage;
        if (!Supports(image))
            throw new NotSupportedException("PDB output requires an x64 PE binary.");
        var position = image.Position;
        try
        {
            image.Position = 0;
            using var reader = new System.Reflection.PortableExecutable.PEReader((Stream)image, PEStreamOptions.LeaveOpen);
            var headers = reader.PEHeaders;
            var guid = Guid.Empty;
            uint age = 1;
            var matching = false;
            foreach (var entry in reader.ReadDebugDirectory())
            {
                if (entry.Type != DebugDirectoryEntryType.CodeView)
                    continue;
                var codeView = reader.ReadCodeViewDebugDirectoryData(entry);
                guid = codeView.Guid;
                age = checked((uint)codeView.Age);
                matching = true;
                break;
            }
            status?.Invoke(this, matching ? $"PDB identity: {guid}, age {age}" : "PE has no RSDS record; PDB uses an empty GUID and requires manual symbol loading.");
            var sectionOffset = checked((int)image.ReadUInt32(0x3C) + 24 + headers.CoffHeader.SizeOfOptionalHeader);
            var sectionHeaders = image.ReadBytes(sectionOffset, checked(headers.SectionHeaders.Length * 40));
            var types = new PdbTypes();
            var procedures = new List<PdbProcedure>();
            var boundaries = new SortedSet<ulong>(model.Package.FunctionAddresses.Keys);
            var typed = 0;
            foreach (var method in model.EnumerateNativeMethods())
            {
                if (method.Address < image.ImageBase || method.Address - image.ImageBase > uint.MaxValue)
                    continue;
                var rva = (uint)(method.Address - image.ImageBase);
                for (var i = 0; i < headers.SectionHeaders.Length; i++)
                {
                    var section = headers.SectionHeaders[i];
                    if (
                        (section.SectionCharacteristics & SectionCharacteristics.MemExecute) == 0
                        || rva < (uint)section.VirtualAddress
                        || rva >= (ulong)(uint)section.VirtualAddress + (uint)section.VirtualSize
                    )
                        continue;
                    var type = method.SignatureComplete && method.Signature != null ? types.Procedure(method.Signature) : 0;
                    if (type != 0)
                        typed++;
                    procedures.Add(new(method.Name, checked((ushort)(i + 1)), rva - (uint)section.VirtualAddress, rva, type));
                    boundaries.Add(method.Address);
                    break;
                }
            }
            var addresses = boundaries
                .Select(a => a >= image.ImageBase && a - image.ImageBase <= uint.MaxValue ? (uint?)(a - image.ImageBase) : null)
                .Where(a => a.HasValue)
                .Select(a => a.Value)
                .ToArray();
            foreach (var procedure in procedures)
            {
                var section = headers.SectionHeaders[procedure.Segment - 1];
                var end = (ulong)(uint)section.VirtualAddress + (uint)section.VirtualSize;
                var next = Array.BinarySearch(addresses, procedure.Rva);
                next = next >= 0 ? next + 1 : ~next;
                var limit = next < addresses.Length ? Math.Min(end, addresses[next]) : end;
                procedure.Size = checked((uint)Math.Min(0x200000UL, limit - procedure.Rva));
            }
            status?.Invoke(this, $"PDB functions: {procedures.Count} ({typed} typed)");
            var globals = new List<PdbGlobal>();
            var dataAddresses = new HashSet<ulong>();
            foreach (var variable in NativeDebugGlobals.Enumerate(model))
            {
                if (
                    variable.Type == null
                    || variable.Type.SizeBytes <= 0
                    || variable.Address < image.ImageBase
                    || variable.Address - image.ImageBase > uint.MaxValue
                    || dataAddresses.Contains(variable.Address)
                )
                    continue;
                var rva = (uint)(variable.Address - image.ImageBase);
                for (var i = 0; i < headers.SectionHeaders.Length; i++)
                {
                    var section = headers.SectionHeaders[i];
                    var end = (ulong)(uint)section.VirtualAddress + (uint)section.VirtualSize;
                    if ((section.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0 || rva < (uint)section.VirtualAddress || rva >= end || (ulong)variable.Type.SizeBytes > end - rva)
                        continue;
                    globals.Add(new(variable.Name, checked((ushort)(i + 1)), rva - (uint)section.VirtualAddress, types.Resolve(variable.Type)));
                    dataAddresses.Add(variable.Address);
                    break;
                }
            }
            types.Include(model.EnumerateNativeTypes());
            status?.Invoke(this, $"PDB type records: {types.Records.Count}");
            status?.Invoke(this, $"PDB globals: {globals.Count}");
            PdbStreams.Write(outputFile, guid, age, procedures, types.Records, sectionHeaders, globals);
            return new(procedures.Count, typed, types.Records.Count, guid, age, matching) { Globals = globals.Count };
        }
        finally
        {
            image.Position = position;
        }
    }
}
