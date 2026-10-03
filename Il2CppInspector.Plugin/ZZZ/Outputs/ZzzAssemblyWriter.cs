using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Il2CppInspector.Next.BinaryMetadata;

namespace Il2CppInspector.Outputs
{
    // The MORAX writer emits ECMA-335 tables directly. Its memory scales with one assembly's
    // table rows rather than a dnlib object graph for every field, parameter and attribute.
    internal sealed class ZzzAssemblyWriter(Il2CppInspector package, bool suppressMetadata, Reflection.TypeModel model)
    {
        internal static string ZzzStorageBase(uint tag) =>
            tag switch
            {
                0 => "klass->static_fields",
                1 => "thread static storage",
                2 => "MetadataRegistration->globalStaticStorage (+0x60)",
                4 => "*MetadataRegistration->globalStaticStorageSlot (+0x78)",
                _ => "unknown",
            };

        internal void Write(string directory, EventHandler<string> status)
        {
            var directAttributes = model
                .TypesByDefinitionIndex.Where(t =>
                    t != null && IsAttribute(t) && t.DeclaredFields.Count == 0 && t.DeclaredProperties.Count == 0 && t.DeclaredConstructors.Any(c => !c.IsStatic && c.DeclaredParameters.Count == 0)
                )
                .Select(t => t.Index)
                .ToHashSet();
            var genericAddresses = new Dictionary<int, (ulong Address, int Spec)>();
            for (var i = 0; i < package.MethodSpecs.Length; i++)
            {
                var spec = package.MethodSpecs[i];
                if (package.GenericMethodPointers.TryGetValue(spec, out var pointer))
                {
                    genericAddresses.TryAdd(spec.MethodDefinitionIndex, (pointer, i));
                }
            }
            for (var i = 0; i < package.Images.Length; i++)
            {
                var name = package.Strings[package.Images[i].NameIndex];
                status?.Invoke(this, "Generating " + name);
                new ModuleWriter(package, i, suppressMetadata, genericAddresses, directAttributes, model).Write(Path.Combine(directory, name));
            }

            static bool IsAttribute(Reflection.TypeInfo type) => type != null && (type.FullName == "System.Attribute" || IsAttribute(type.BaseType));
        }

        private sealed class ModuleWriter
        {
            private readonly Il2CppInspector package;
            private readonly ZzzMorax adapter;
            private readonly int imageIndex;
            private readonly bool suppress;
            private readonly Dictionary<int, (ulong Address, int Spec)> genericAddresses;
            private readonly HashSet<int> directAttributes;
            private readonly Reflection.TypeModel model;
            private readonly Dictionary<int, MemberReferenceHandle> attributeConstructors = [];
            private readonly MetadataBuilder metadata = new();
            private readonly BlobBuilder il = new();
            private readonly BlobBuilder fieldData = new();
            private readonly Dictionary<int, EntityHandle> definitions = [];
            private readonly Dictionary<int, EntityHandle> typeReferences = [];
            private readonly Dictionary<int, AssemblyReferenceHandle> assemblyReferences = [];
            private readonly Dictionary<string, MemberReferenceHandle> helperConstructors = [];
            private readonly List<(EntityHandle Owner, int Parameter)> genericParameters = [];
            private readonly Dictionary<int, MethodDefinitionHandle> methodHandles = [];
            private readonly int[] typeImages;
            private readonly int start;
            private readonly int end;
            private readonly int enumBase;
            private AssemblyReferenceHandle helperAssembly;

            internal ModuleWriter(
                Il2CppInspector package,
                int image,
                bool suppress,
                Dictionary<int, (ulong Address, int Spec)> genericAddresses,
                HashSet<int> directAttributes,
                Reflection.TypeModel model
            )
            {
                this.package = package;
                adapter = (ZzzMorax)package.Metadata.GameAdapter;
                imageIndex = image;
                this.suppress = suppress;
                this.genericAddresses = genericAddresses;
                this.directAttributes = directAttributes;
                this.model = model;
                var definition = package.Images[image];
                start = definition.TypeStart;
                end = checked(start + (int)definition.TypeCount);
                enumBase = Enumerable
                    .Range(0, package.TypeDefinitions.Length)
                    .First(t => Name(package.TypeDefinitions[t].NameIndex) == "Enum" && Name(package.TypeDefinitions[t].NamespaceIndex) == "System");
                typeImages = new int[package.TypeDefinitions.Length];
                Array.Fill(typeImages, -1);
                for (var i = 0; i < package.Images.Length; i++)
                {
                    var img = package.Images[i];
                    Array.Fill(typeImages, i, img.TypeStart, checked((int)img.TypeCount));
                }
                var rid = 2;
                for (var t = start; t < end; t++)
                {
                    if (Name(package.TypeDefinitions[t].NameIndex) == "<Module>")
                    {
                        definitions.Add(t, MetadataTokens.TypeDefinitionHandle(1));
                    }
                    else
                    {
                        definitions.Add(t, MetadataTokens.TypeDefinitionHandle(rid++));
                    }
                }
            }

            private string Name(int index) => package.Strings[index];

            private StringHandle String(string text) => metadata.GetOrAddString(text ?? "");

            private BlobHandle Blob(BlobBuilder blob) => metadata.GetOrAddBlob(blob);

            private static int Row(EntityHandle handle) => MetadataTokens.GetRowNumber(handle);

            internal void Write(string path)
            {
                var image = package.Images[imageIndex];
                var assembly = package.Assemblies[image.AssemblyIndex];
                var name = assembly.Aname;
                var moduleName = Name(image.NameIndex);
                var mvid = new Guid(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(moduleName)));
                var moduleHandle = metadata.AddModule(0, String(moduleName), metadata.GetOrAddGuid(mvid), default, default);
                var assemblyHandle = metadata.AddAssembly(
                    String(Name(name.NameIndex)),
                    new Version(name.Major, name.Minor, name.Build, name.Revision),
                    // The full public key is unavailable; do not claim a strong-name key on a shim with an empty key blob.
                    String(Name(name.CultureIndex)),
                    default,
                    (AssemblyFlags)(name.Flags & ~AssemblyNameFlags.PublicKey),
                    (System.Reflection.AssemblyHashAlgorithm)name.HashAlg
                );
                Helper(assemblyHandle, "AssemblyFlagsAttribute", ("Flags", $"0x{(uint)name.Flags:X}"));
                Token(moduleHandle, image.Token);
                Token(assemblyHandle, assembly.Token);
                Attributes(assemblyHandle, image, assembly.Token);
                il.WriteInt32(0);
                il.WriteByte(0x0A); // tiny body: ldnull; throw
                il.WriteByte(0x14);
                il.WriteByte(0x7A);
                metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, String("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
                for (var t = start; t < end; t++)
                {
                    var td = package.TypeDefinitions[t];
                    if (Name(td.NameIndex) == "<Module>")
                    {
                        continue;
                    }

                    var handle = metadata.AddTypeDefinition(
                        td.Flags,
                        String(td.DeclaringTypeIndex >= 0 ? "" : model.TypesByDefinitionIndex[t].Namespace),
                        String(model.TypesByDefinitionIndex[t].BaseName),
                        td.Bitfield.EnumType ? DefinitionHandle(enumBase)
                            : td.ParentIndex < 0 ? default
                            : TypeHandle(td.ParentIndex),
                        MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1),
                        MetadataTokens.MethodDefinitionHandle(metadata.GetRowCount(TableIndex.MethodDef) + 1)
                    );
                    if (handle != definitions[t])
                    {
                        throw new InvalidOperationException("ZZZ TypeDef row order mismatch.");
                    }

                    if (td.DeclaringTypeIndex >= 0)
                    {
                        metadata.AddNestedType(handle, (TypeDefinitionHandle)DefinitionHandle(package.TypeReferences[td.DeclaringTypeIndex].Data.KlassIndex));
                    }

                    if ((td.Flags & TypeAttributes.LayoutMask) is TypeAttributes.SequentialLayout or TypeAttributes.ExplicitLayout)
                    {
                        var packing = (int)td.Bitfield.PackingSize;
                        metadata.AddTypeLayout(
                            handle,
                            td.Bitfield.DefaultPackingSize || packing == 0 ? (ushort)0 : checked((ushort)(1 << (packing - 1))),
                            checked((uint)Math.Max(0, (long)package.TypeDefinitionSizes[t].InstanceSize - 16))
                        );
                    }
                    for (var i = 0; i < td.InterfacesCount; i++)
                    {
                        metadata.AddInterfaceImplementation(handle, TypeHandle(package.InterfaceUsageIndices[td.InterfacesIndex + i]));
                    }

                    QueueGenerics(handle, td.GenericContainerIndex);
                    Token(handle, td.Token);
                    Attributes(handle, image, td.Token);
                    Fields(td, image);
                    Methods(td, image);
                    Properties(handle, td, image);
                    Events(handle, td, image);
                }
                foreach (
                    var (owner, parameter) in genericParameters
                        .OrderBy(p => Row(p.Owner) * 2 + (p.Owner.Kind == HandleKind.MethodDefinition ? 1 : 0))
                        .ThenBy(p => package.GenericParameters[p.Parameter].Num)
                )
                {
                    var p = package.GenericParameters[parameter];
                    var gp = metadata.AddGenericParameter(owner, (GenericParameterAttributes)p.Flags, String(Name(p.NameIndex)), p.Num);
                    for (var c = 0; c < p.ConstraintsCount; c++)
                    {
                        metadata.AddGenericParameterConstraint(gp, TypeHandle(package.GenericConstraintIndices[p.ConstraintsStart + c]));
                    }
                }
                var pe = new ManagedPEBuilder(
                    new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
                    new MetadataRootBuilder(metadata),
                    il,
                    mappedFieldData: fieldData,
                    flags: CorFlags.ILOnly
                );
                var output = new BlobBuilder();
                pe.Serialize(output);
                using var stream = File.Create(path);
                output.WriteContentTo(stream);
            }

            private void Fields(Next.Metadata.Il2CppTypeDefinition td, Next.Metadata.Il2CppImageDefinition image)
            {
                for (var i = 0; i < td.FieldCount; i++)
                {
                    var index = (int)td.FieldIndex + i;
                    var field = package.Fields[index];
                    var type = package.TypeReferences[field.TypeIndex];
                    var signature = new BlobBuilder();
                    signature.WriteByte(0x06);
                    Type(signature, field.TypeIndex);
                    var attrs = (FieldAttributes)type.Attrs;
                    var handle = metadata.AddFieldDefinition(attrs, String(model.TypesByReferenceIndex[td.ByValTypeIndex].DeclaredFields[i].Name), Blob(signature));
                    if (package.FieldDefaultValue.TryGetValue(index, out var value) && (attrs & FieldAttributes.HasDefault) != 0)
                    {
                        metadata.AddConstant(handle, value.Item2);
                    }
                    else if ((attrs & (FieldAttributes.HasFieldRVA | FieldAttributes.Literal)) != 0)
                    {
                        Helper(handle, "MetadataOffsetAttribute", ("Offset", $"0x{value.Item1:X8}"));
                    }

                    Token(handle, field.Token);
                    if ((attrs & FieldAttributes.Literal) == 0)
                    {
                        var raw = adapter.RawFieldOffsets[index];
                        if ((attrs & FieldAttributes.Static) == 0)
                        {
                            Helper(handle, "FieldOffsetAttribute", ("Offset", $"0x{(td.Bitfield.ValueType && raw >= 16 ? raw - 16 : raw):X}"));
                        }
                        else
                        {
                            Helper(
                                handle,
                                "StaticFieldOffsetAttribute",
                                ("Offset", $"0x{package.FieldOffsets[index] & 0x7FFFFFFF:X}"),
                                ("ThreadStatic", raw >> 24 == 1),
                                ("StorageTag", $"{raw >> 24}"),
                                ("StorageBase", ZzzStorageBase(raw >> 24))
                            );
                        }
                    }
                    if ((attrs & FieldAttributes.HasFieldRVA) != 0 && package.FieldDefaultValue.TryGetValue(index, out var initial) && initial.Item1 != 0)
                    {
                        var size = adapter.FieldRvaSize(type, package.Binary);
                        if (size > 0)
                        {
                            fieldData.Align(4);
                            metadata.AddFieldRelativeVirtualAddress(handle, fieldData.Count);
                            var bytes = package.Metadata.ReadBytes(checked((int)initial.Item1), size);
                            fieldData.WriteBytes(bytes);
                            Helper(handle, "MetadataPreviewAttribute", ("Data", Convert.ToHexString(bytes)));
                        }
                    }
                    Attributes(handle, image, field.Token);
                }
            }

            private void Methods(Next.Metadata.Il2CppTypeDefinition td, Next.Metadata.Il2CppImageDefinition image)
            {
                for (var local = 0; local < td.MethodCount; local++)
                {
                    var index = (int)td.MethodIndex + local;
                    var method = package.Methods[index];
                    var signature = new BlobBuilder();
                    var genericCount = method.GenericContainerIndex < 0 ? 0 : package.GenericContainers[method.GenericContainerIndex].TypeArgc;
                    signature.WriteByte((byte)(((method.Flags & (ushort)MethodAttributes.Static) == 0 ? 0x20 : 0) | (genericCount > 0 ? 0x10 : 0)));
                    if (genericCount > 0)
                    {
                        signature.WriteCompressedInteger(genericCount);
                    }

                    signature.WriteCompressedInteger(method.ParameterCount);
                    Type(signature, method.ReturnType);
                    for (var p = 0; p < method.ParameterCount; p++)
                    {
                        Type(signature, package.Params[method.ParameterStart + p].TypeIndex);
                    }

                    var noBody =
                        (method.Flags & (ushort)(MethodAttributes.Abstract | MethodAttributes.PinvokeImpl)) != 0
                        || ((MethodImplAttributes)method.ImplFlags & (MethodImplAttributes.InternalCall | MethodImplAttributes.CodeTypeMask)) != 0;
                    var handle = metadata.AddMethodDefinition(
                        (MethodAttributes)method.Flags,
                        (MethodImplAttributes)method.ImplFlags,
                        String(model.MethodsByDefinitionIndex[index].Name),
                        Blob(signature),
                        noBody ? -1 : 4,
                        MetadataTokens.ParameterHandle(metadata.GetRowCount(TableIndex.Param) + 1)
                    );
                    methodHandles.Add(index, handle);
                    QueueGenerics(handle, method.GenericContainerIndex);
                    for (var p = 0; p < method.ParameterCount; p++)
                    {
                        var pi = (int)method.ParameterStart + p;
                        var parameter = package.Params[pi];
                        var attrs = (ParameterAttributes)package.TypeReferences[parameter.TypeIndex].Attrs;
                        var ph = metadata.AddParameter(attrs, String(model.MethodsByDefinitionIndex[index].DeclaredParameters[p].Name), p + 1);
                        if (package.ParameterDefaultValue.TryGetValue(pi, out var value) && (attrs & ParameterAttributes.HasDefault) != 0)
                        {
                            metadata.AddConstant(ph, value.Item2);
                        }

                        if (value.Item1 != 0)
                        {
                            Helper(ph, "MetadataOffsetAttribute", ("Offset", $"0x{value.Item1:X8}"));
                        }

                        Token(ph, parameter.Token);
                        Attributes(ph, image, parameter.Token);
                    }
                    Token(handle, method.Token);
                    var module = package.Modules[Name(image.NameIndex)];
                    if (package.GetMethodPointer(module, method) is { } address)
                    {
                        Helper(
                            handle,
                            "AddressAttribute",
                            ("RVA", $"0x{address.Start - package.BinaryImage.ImageBase:X}"),
                            ("Offset", $"0x{package.BinaryImage.MapVATR(address.Start):X}"),
                            ("VA", $"0x{address.Start:X}"),
                            ("Slot", method.Slot == ushort.MaxValue ? "" : method.Slot.ToString())
                        );
                    }
                    else if (genericAddresses.TryGetValue(index, out var generic))
                    {
                        Helper(
                            handle,
                            "GenericInstAddressAttribute",
                            ("RVA", $"0x{generic.Address - package.BinaryImage.ImageBase:X}"),
                            ("Offset", $"0x{package.BinaryImage.MapVATR(generic.Address):X}"),
                            ("VA", $"0x{generic.Address:X}"),
                            ("Spec", generic.Spec.ToString())
                        );
                    }

                    Attributes(handle, image, method.Token);
                }
            }

            private void Properties(TypeDefinitionHandle owner, Next.Metadata.Il2CppTypeDefinition td, Next.Metadata.Il2CppImageDefinition image)
            {
                if (td.PropertyCount == 0)
                {
                    return;
                }

                metadata.AddPropertyMap(owner, MetadataTokens.PropertyDefinitionHandle(metadata.GetRowCount(TableIndex.Property) + 1));
                for (var i = 0; i < td.PropertyCount; i++)
                {
                    var property = package.Properties[td.PropertyIndex + i];
                    var accessor = property.Get >= 0 ? package.Methods[td.MethodIndex + property.Get] : package.Methods[td.MethodIndex + property.Set];
                    var signature = new BlobBuilder();
                    signature.WriteByte((byte)(0x08 | ((accessor.Flags & (ushort)MethodAttributes.Static) == 0 ? 0x20 : 0)));
                    var count = property.Get >= 0 ? accessor.ParameterCount : accessor.ParameterCount - 1;
                    signature.WriteCompressedInteger(count);
                    Type(signature, property.Get >= 0 ? accessor.ReturnType : package.Params[accessor.ParameterStart + count].TypeIndex);
                    for (var p = 0; p < count; p++)
                    {
                        Type(signature, package.Params[accessor.ParameterStart + p].TypeIndex);
                    }

                    var handle = metadata.AddProperty(property.Attrs, String(model.TypesByReferenceIndex[td.ByValTypeIndex].DeclaredProperties[i].Name), Blob(signature));
                    if (property.Get >= 0)
                    {
                        metadata.AddMethodSemantics(handle, MethodSemanticsAttributes.Getter, methodHandles[td.MethodIndex + property.Get]);
                    }

                    if (property.Set >= 0)
                    {
                        metadata.AddMethodSemantics(handle, MethodSemanticsAttributes.Setter, methodHandles[td.MethodIndex + property.Set]);
                    }

                    Token(handle, property.Token);
                    Attributes(handle, image, property.Token);
                }
            }

            private void Events(TypeDefinitionHandle owner, Next.Metadata.Il2CppTypeDefinition td, Next.Metadata.Il2CppImageDefinition image)
            {
                if (td.EventCount == 0)
                {
                    return;
                }

                metadata.AddEventMap(owner, MetadataTokens.EventDefinitionHandle(metadata.GetRowCount(TableIndex.Event) + 1));
                for (var i = 0; i < td.EventCount; i++)
                {
                    var e = package.Events[td.EventIndex + i];
                    var handle = metadata.AddEvent(
                        (EventAttributes)package.TypeReferences[e.TypeIndex].Attrs,
                        String(model.TypesByReferenceIndex[td.ByValTypeIndex].DeclaredEvents[i].Name),
                        TypeHandle(e.TypeIndex)
                    );
                    if (e.Add >= 0)
                    {
                        metadata.AddMethodSemantics(handle, MethodSemanticsAttributes.Adder, methodHandles[td.MethodIndex + e.Add]);
                    }

                    if (e.Remove >= 0)
                    {
                        metadata.AddMethodSemantics(handle, MethodSemanticsAttributes.Remover, methodHandles[td.MethodIndex + e.Remove]);
                    }

                    if (e.Raise >= 0)
                    {
                        metadata.AddMethodSemantics(handle, MethodSemanticsAttributes.Raiser, methodHandles[td.MethodIndex + e.Raise]);
                    }

                    Token(handle, e.Token);
                    Attributes(handle, image, e.Token);
                }
            }

            private void QueueGenerics(EntityHandle owner, int containerIndex)
            {
                if (containerIndex < 0)
                {
                    return;
                }

                var c = package.GenericContainers[containerIndex];
                for (var i = 0; i < c.TypeArgc; i++)
                {
                    genericParameters.Add((owner, c.GenericParameterStart + i));
                }
            }

            private EntityHandle DefinitionHandle(int definition)
            {
                if (definitions.TryGetValue(definition, out var local))
                {
                    return local;
                }

                if (typeReferences.TryGetValue(definition, out var cached))
                {
                    return cached;
                }

                var t = package.TypeDefinitions[definition];
                EntityHandle scope = t.DeclaringTypeIndex >= 0 ? DefinitionHandle(package.TypeReferences[t.DeclaringTypeIndex].Data.KlassIndex) : AssemblyReference(typeImages[definition]);
                var handle = metadata.AddTypeReference(
                    scope,
                    String(t.DeclaringTypeIndex >= 0 ? "" : model.TypesByDefinitionIndex[definition].Namespace),
                    String(model.TypesByDefinitionIndex[definition].BaseName)
                );
                typeReferences.Add(definition, handle);
                return handle;
            }

            private AssemblyReferenceHandle AssemblyReference(int index)
            {
                if (assemblyReferences.TryGetValue(index, out var cached))
                {
                    return cached;
                }

                if (index < 0)
                {
                    throw new InvalidDataException("Unowned ZZZ type referenced by a managed signature.");
                }

                var a = package.Assemblies[package.Images[index].AssemblyIndex].Aname;
                var handle = metadata.AddAssemblyReference(
                    String(Name(a.NameIndex)),
                    new(a.Major, a.Minor, a.Build, a.Revision),
                    String(Name(a.CultureIndex)),
                    // Match the unsigned assembly definitions emitted by this writer.
                    default,
                    0,
                    default
                );
                assemblyReferences.Add(index, handle);
                return handle;
            }

            private EntityHandle TypeHandle(int index)
            {
                var type = package.TypeReferences[index];
                if (!type.ByRef && type.Type is Il2CppTypeEnum.IL2CPP_TYPE_CLASS or Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
                {
                    return DefinitionHandle(type.Data.KlassIndex);
                }

                var signature = new BlobBuilder();
                Type(signature, index);
                return metadata.AddTypeSpecification(Blob(signature));
            }

            private void Type(BlobBuilder blob, int index, int depth = 0)
            {
                if (depth > 64)
                {
                    throw new InvalidDataException("Cyclic ZZZ type signature.");
                }

                var type = package.TypeReferences[index];
                if (type.ByRef)
                {
                    blob.WriteByte(0x10);
                }

                var kind = type.Type;
                blob.WriteByte((byte)kind);
                switch (kind)
                {
                    case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                    case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                        TypeCode(blob, DefinitionHandle(type.Data.KlassIndex));
                        break;
                    case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                        blob.WriteCompressedInteger(package.GenericParameters[type.Data.GenericParameterIndex].Num);
                        break;
                    case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                        Type(blob, package.TypeReferenceIndicesByAddress[type.Data.Value], depth + 1);
                        break;
                    case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                        var array = package.BinaryImage.ReadMappedVersionedObject<Il2CppArrayType>(type.Data.ArrayType);
                        Type(blob, package.TypeReferenceIndicesByAddress[array.ElementType], depth + 1);
                        blob.WriteCompressedInteger(array.Rank);
                        blob.WriteCompressedInteger(0);
                        blob.WriteCompressedInteger(0);
                        break;
                    case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                        var pair = adapter.GenericClass(type.Data.Value);
                        blob.WriteByte(package.TypeDefinitions[pair.Definition].Bitfield.ValueType ? (byte)0x11 : (byte)0x12);
                        TypeCode(blob, DefinitionHandle(pair.Definition));
                        var inst = package.GenericInstances[pair.Instance];
                        blob.WriteCompressedInteger(checked((int)inst.TypeArgc));
                        foreach (var argument in package.BinaryImage.ReadMappedUWordArray(inst.TypeArgv, checked((int)inst.TypeArgc)))
                        {
                            Type(blob, package.TypeReferenceIndicesByAddress[argument], depth + 1);
                        }

                        break;
                }
            }

            private static void TypeCode(BlobBuilder blob, EntityHandle type)
            {
                var tag = type.Kind switch
                {
                    HandleKind.TypeDefinition => 0,
                    HandleKind.TypeReference => 1,
                    HandleKind.TypeSpecification => 2,
                    _ => throw new InvalidOperationException(),
                };
                blob.WriteCompressedInteger(Row(type) * 4 + tag);
            }

            private void Token(EntityHandle parent, uint token) => Helper(parent, "TokenAttribute", ("Token", $"0x{token:X8}"));

            private void Helper(EntityHandle parent, string name, params (string Name, object Value)[] arguments)
            {
                if (suppress)
                {
                    return;
                }

                if (!helperConstructors.TryGetValue(name, out var ctor))
                {
                    if (helperAssembly.IsNil)
                    {
                        helperAssembly = metadata.AddAssemblyReference(String("Il2CppInspector"), new(1, 0, 0, 0), default, default, 0, default);
                    }

                    var type = metadata.AddTypeReference(helperAssembly, String("Il2CppInspector.DLL"), String(name));
                    ctor = metadata.AddMemberReference(type, String(".ctor"), metadata.GetOrAddBlob(new byte[] { 0x20, 0, 1 }));
                    helperConstructors.Add(name, ctor);
                }
                var value = new BlobBuilder();
                value.WriteUInt16(1);
                value.WriteUInt16(checked((ushort)arguments.Length));
                foreach (var (field, data) in arguments)
                {
                    value.WriteByte(0x53);
                    value.WriteByte(data is bool ? (byte)0x02 : (byte)0x0E);
                    SerString(value, field);
                    if (data is bool boolean)
                    {
                        value.WriteByte(boolean ? (byte)1 : (byte)0);
                    }
                    else
                    {
                        SerString(value, (string)data);
                    }
                }
                metadata.AddCustomAttribute(parent, ctor, Blob(value));
            }

            private static void SerString(BlobBuilder blob, string text)
            {
                if (text == null)
                {
                    blob.WriteByte(0xFF);
                    return;
                }
                var bytes = Encoding.UTF8.GetBytes(text);
                blob.WriteCompressedInteger(bytes.Length);
                blob.WriteBytes(bytes);
            }

            private void Attributes(EntityHandle parent, Next.Metadata.Il2CppImageDefinition image, uint token)
            {
                if (!package.AttributeIndicesByToken.TryGetValue(image.CustomAttributeStart, out var tokens) || !tokens.TryGetValue(token, out var index))
                {
                    return;
                }

                var range = package.AttributeTypeRanges[index];
                for (var i = 0; i < range.Count; i++)
                {
                    var typeIndex = package.AttributeTypeIndices[range.Start + i];
                    var type = package.TypeReferences[typeIndex];
                    var td = package.TypeDefinitions[type.Data.KlassIndex];
                    if (directAttributes.Contains(type.Data.KlassIndex))
                    {
                        if (!attributeConstructors.TryGetValue(type.Data.KlassIndex, out var ctor))
                        {
                            ctor = metadata.AddMemberReference(DefinitionHandle(type.Data.KlassIndex), String(".ctor"), metadata.GetOrAddBlob(new byte[] { 0x20, 0, 1 }));
                            attributeConstructors.Add(type.Data.KlassIndex, ctor);
                        }
                        metadata.AddCustomAttribute(parent, ctor, metadata.GetOrAddBlob(new byte[] { 1, 0, 0, 0 }));
                        continue;
                    }
                    var address = package.CustomAttributeGenerators[index];
                    Helper(
                        parent,
                        "AttributeAttribute",
                        ("Name", model.TypesByDefinitionIndex[type.Data.KlassIndex].Name),
                        ("RVA", $"0x{address - package.BinaryImage.ImageBase:X}"),
                        ("Offset", $"0x{package.BinaryImage.MapVATR(address):X}")
                    );
                }
            }
        }
    }
}
