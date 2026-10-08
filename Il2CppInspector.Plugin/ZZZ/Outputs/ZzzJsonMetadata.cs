using System.Text.Json;
using Il2CppInspector.Cpp;
using Il2CppInspector.Model;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Outputs
{
    // Keep the existing Inspector script schema while streaming method specs directly.
    // No AppMethod/CppFnPtrType/constructed MethodInfo graph is retained for 842k entries.
    internal sealed class ZzzJsonMetadata(AppModel model, bool allowComments)
    {
        private ZzzMorax Adapter => (ZzzMorax)Package.Metadata.GameAdapter;

        public ZzzJsonMetadata(AppModel model)
            : this(model, false) { }

        private Il2CppInspector Package => model.Package;
        private Utf8JsonWriter writer;
        private ZzzNativeModel Native => (ZzzNativeModel)model.GameNativeModel;
        internal bool SupplementDebugInfo { get; set; }

        // Header generation precedes JSON output. Discover arrays produced by generic signature substitution first.
        internal void PrepareNativeArrayTypes()
        {
            var definitions = new Dictionary<int, int[]>();
            for (var i = 0; i < Package.Methods.Length; i++)
            {
                var method = Package.Methods[i];
                var indices = Enumerable
                    .Range(method.ParameterStart, method.ParameterCount)
                    .Select(p => (int)Package.Params[p].TypeIndex)
                    .Prepend((int)method.ReturnType)
                    .Where(t => Package.TypeReferences[t].Type is Il2CppTypeEnum.IL2CPP_TYPE_ARRAY or Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY or Il2CppTypeEnum.IL2CPP_TYPE_PTR)
                    .Distinct()
                    .ToArray();
                if (indices.Length == 0)
                {
                    continue;
                }

                definitions.Add(i, indices);
                if (model.TypeModel.MethodsByDefinitionIndex[i].VirtualAddress.HasValue)
                {
                    foreach (var index in indices)
                    {
                        model.GameNativeModel.CType(ResolveType(index, [], []));
                    }
                }
            }
            foreach (var spec in Package.GenericMethodPointers.Keys)
            {
                if (definitions.TryGetValue(spec.MethodDefinitionIndex, out var indices))
                {
                    foreach (var index in indices)
                    {
                        model.GameNativeModel.CType(ResolveType(index, Arguments(spec.ClassIndexIndex), Arguments(spec.MethodIndexIndex)));
                    }
                }
            }
        }

        internal void Write(string path)
        {
            using var stream = File.Create(path);
            using var json = new Utf8JsonWriter(stream, new() { Indented = true });
            writer = json;
            writer.WriteStartObject();
            writer.WriteStartObject("addressMap");
            Array(
                "methodDefinitions",
                () =>
                {
                    for (var i = 0; i < Package.Methods.Length; i++)
                    {
                        var method = model.TypeModel.MethodsByDefinitionIndex[i];
                        if (method.VirtualAddress is { } address)
                        {
                            Method(i, -1, address.Start);
                        }
                    }
                }
            );
            Array(
                "constructedGenericMethods",
                () =>
                {
                    for (var i = 0; i < Package.MethodSpecs.Length; i++)
                    {
                        if (Package.GenericMethodPointers.TryGetValue(Package.MethodSpecs[i], out var address))
                        {
                            Method(Package.MethodSpecs[i].MethodDefinitionIndex, i, address);
                        }
                    }
                }
            );
            Array(
                "customAttributesGenerators",
                () =>
                {
                    foreach (var attribute in model.TypeModel.AttributesByIndices.Values)
                    {
                        Object(() => Function(attribute.VirtualAddress.Start, attribute.Name, attribute.Signature));
                    }
                }
            );
            Array(
                "methodInvokers",
                () =>
                {
                    var seen = new HashSet<ulong>();
                    for (var i = 0; i < Package.MethodInvokePointers.Length; i++)
                    {
                        var address = Package.MethodInvokePointers[i];
                        if (address == 0 || !seen.Add(address))
                        {
                            continue;
                        }

                        var name = $"Morax_Invoker_{i}";
                        Object(() => Function(address, name, $"void * {name}(Il2CppMethodPointer methodPointer, const MethodInfo * method, void * obj, void ** args)"));
                    }
                }
            );
            Array(
                "stringLiterals",
                () =>
                {
                    foreach (var text in model.Strings)
                    {
                        Object(() =>
                        {
                            Name(text.Key, $"StringLiteral_{text.Key:X}");
                            writer.WriteString("string", text.Value);
                        });
                    }
                }
            );
            Array(
                "typeInfoPointers",
                () =>
                {
                    foreach (var usage in Package.MetadataUsages.Where(u => u.Type == MetadataUsageType.TypeInfo))
                    {
                        var type = model.TypeModel.TypesByReferenceIndex[usage.SourceIndex];
                        var ctype = model.GameNativeModel.ClassName(type);
                        Object(() =>
                        {
                            Typed(usage.VirtualAddress, $"TypeInfo_{usage.SourceIndex}_{usage.VirtualAddress:X}", $"struct {ctype} *");
                            writer.WriteString("dotNetType", type.CSharpName);
                        });
                    }
                }
            );
            Array(
                "typeRefPointers",
                () =>
                {
                    foreach (var usage in Package.MetadataUsages.Where(u => u.Type == MetadataUsageType.Type))
                    {
                        Object(() =>
                        {
                            Name(usage.VirtualAddress, $"TypeRef_{usage.SourceIndex}_{usage.VirtualAddress:X}");
                            writer.WriteString("dotNetType", model.TypeModel.TypesByReferenceIndex[usage.SourceIndex].CSharpName);
                        });
                    }
                }
            );
            Array(
                "methodInfoPointers",
                () =>
                {
                    foreach (var usage in Package.MetadataUsages.Where(u => u.Type is MetadataUsageType.MethodDef or MetadataUsageType.MethodRef))
                    {
                        var spec = usage.Type == MetadataUsageType.MethodRef ? usage.SourceIndex : -1;
                        var definition = spec >= 0 ? Package.MethodSpecs[spec].MethodDefinitionIndex : usage.SourceIndex;
                        Object(() =>
                        {
                            Name(usage.VirtualAddress, MethodName(definition, spec) + "_MethodInfo");
                            writer.WriteString("dotNetSignature", DotNetSignature(definition, spec));
                            if (spec >= 0 && Package.GenericMethodPointers.TryGetValue(Package.MethodSpecs[spec], out var address))
                            {
                                writer.WriteString("methodAddress", address.ToAddressString());
                            }
                            else if (spec < 0 && model.TypeModel.MethodsByDefinitionIndex[definition].VirtualAddress is { } ordinary)
                            {
                                writer.WriteString("methodAddress", ordinary.Start.ToAddressString());
                            }
                        });
                    }
                }
            );
            Array(
                "moraxRuntimeCaches",
                () =>
                {
                    foreach (var cache in Adapter.RuntimeCaches)
                    {
                        Object(() =>
                        {
                            Typed(cache.Address, $"MoraxRuntimeCache_{cache.TypeIndex}_{cache.Address:X}", "void *");
                            writer.WriteString("dotNetType", model.TypeModel.TypesByReferenceIndex[cache.TypeIndex].CSharpName);
                        });
                    }
                }
            );
            Array(
                "functionAddresses",
                () =>
                {
                    foreach (var address in Package.FunctionAddresses.Keys.Where(a => a != 0))
                    {
                        writer.WriteStringValue(address.ToAddressString());
                    }
                }
            );
            Array(
                "typeMetadata",
                () =>
                {
                    Object(() => Typed(Package.Binary.CodeRegistrationPointer, "g_CodeRegistration", "struct Il2CppCodeRegistration"));
                    Object(() => Typed(Package.Binary.MetadataRegistrationPointer, "g_MetadataRegistration", "struct Il2CppMetadataRegistration"));
                    foreach (var module in Package.Binary.CodeGenModulePointers)
                    {
                        Object(() => Typed(module.Value, "g_" + module.Key.ToCIdentifier() + "_CodeGenModule", "struct Il2CppCodeGenModule"));
                    }
                }
            );
            Array("functionMetadata", () => Object(() => Function(Package.Binary.RegistrationFunctionPointer, "Morax_MetadataCache_Register", "void * Morax_MetadataCache_Register(void)")));
            Array(
                "arrayMetadata",
                () =>
                    Object(() =>
                    {
                        Typed(Package.Binary.CodeRegistration.CodeGenModules, "g_CodeGenModules", "struct Il2CppCodeGenModule *");
                        writer.WriteNumber("count", Package.Images.Length);
                    })
            );
            Array(
                "apis",
                () =>
                {
                    foreach (var api in Package.Binary.APIExports)
                    {
                        if (model.RuntimeCppTypes.TypedefAliases.TryGetValue(api.Key, out var declaration) && declaration is CppFnPtrType function)
                        {
                            Object(() => Function(api.Value, api.Key, function.ToSignatureString()));
                        }
                    }
                }
            );
            Array(
                "exports",
                () =>
                {
                    foreach (var export in model.Exports)
                    {
                        Object(() => Name(export.VirtualAddress, export.Name));
                    }
                }
            );
            Array(
                "symbols",
                () =>
                {
                    foreach (var symbol in model.Symbols.Values)
                    {
                        Object(() => Name(symbol.VirtualAddress, symbol.Name));
                    }
                }
            );
            Array(
                "fields",
                () =>
                {
                    foreach (var usage in Package.MetadataUsages.Where(u => u.Type == MetadataUsageType.FieldInfo))
                    {
                        var reference = Package.FieldRefs[usage.SourceIndex];
                        var type = Package.TypeReferences[reference.TypeIndex];
                        var definition = type.Type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST ? Adapter.GenericClass(type.Data.Value).Definition : type.Data.KlassIndex;
                        var fieldIndex = (int)Package.TypeDefinitions[definition].FieldIndex + reference.FieldIndex;
                        var field = Package.Fields[fieldIndex];
                        var value = "";
                        var storage = Package.TypeReferences[field.TypeIndex];
                        if (
                            ((uint)storage.Attrs & (uint)System.Reflection.FieldAttributes.HasFieldRVA) != 0
                            && Package.FieldDefaultValue.TryGetValue(fieldIndex, out var initial)
                            && initial.Item1 != 0
                        )
                        {
                            var size = Adapter.FieldRvaSize(storage, Package.Binary);
                            if (size > 0)
                            {
                                value = Convert.ToHexString(Package.Metadata.ReadBytes((long)initial.Item1, size));
                            }
                        }
                        Object(() =>
                        {
                            Name(
                                usage.VirtualAddress,
                                $"FieldInfo_{definition}_{model.TypeModel.TypesByDefinitionIndex[definition].DeclaredFields[reference.FieldIndex].Name.ToCIdentifier()}_{usage.VirtualAddress:X}"
                            );
                            writer.WriteString("value", value);
                            var raw = Adapter.RawFieldOffsets[fieldIndex];
                            writer.WriteNumber("storageTag", raw >> 24);
                            writer.WriteNumber("offset", raw & 0xFFFFFF);
                            writer.WriteString("storageBase", ZzzAssemblyWriter.ZzzStorageBase(raw >> 24));
                        });
                    }
                }
            );
            Array("fieldRvas", () => { });
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        internal string MethodName(int definition, int spec) => Native.MethodName(definition, spec);

        private void Method(int definition, int specIndex, ulong address)
        {
            var signature = NativeSignature(definition, specIndex);
            var method = model.TypeModel.MethodsByDefinitionIndex[definition];
            Object(() =>
            {
                Function(address, signature.Name, $"{signature.ReturnType} {signature.Name}({signature.Parameters})");
                writer.WriteBoolean("signatureComplete", signature.Complete);
                writer.WriteString("dotNetSignature", DotNetSignature(definition, specIndex));
                writer.WriteString("group", method.DeclaringType.Assembly.ShortName + "/" + method.DeclaringType.FullName.Replace('.', '/'));
                if (specIndex >= 0 && Adapter.GenericAdjustorThunks.TryGetValue(specIndex, out var thunk))
                {
                    writer.WriteString("adjustorThunkAddress", thunk.ToAddressString());
                }
            });
        }

        internal (string Name, string ReturnType, string Parameters, bool Complete) NativeSignature(int definition, int specIndex)
        {
            var d = Package.Methods[definition];
            var method = model.TypeModel.MethodsByDefinitionIndex[definition];
            var spec = specIndex >= 0 ? Package.MethodSpecs[specIndex] : new Il2CppMethodSpec { ClassIndexIndex = -1, MethodIndexIndex = -1 };
            var classArgs = Arguments(spec.ClassIndexIndex);
            var methodArgs = Arguments(spec.MethodIndexIndex);
            var name = MethodName(definition, specIndex);
            var complete = true;
            string CType(TypeInfo type)
            {
                var result = model.GameNativeModel.CType(type);
                if (!model.GameNativeModel.IsSignatureTypeComplete(type))
                {
                    complete = false;
                }

                return result;
            }
            var parameters = new List<string>(d.ParameterCount + 2);
            var names = new HashSet<string> { "__this", "method" };
            if (!method.IsStatic)
            {
                var declaring = method.DeclaringType;
                if (classArgs.Length > 0)
                {
                    declaring = declaring.MakeGenericType(classArgs.Select(a => model.TypeModel.TypesByReferenceIndex[a]).ToArray());
                }

                var type = CType(declaring.IsValueType ? declaring.MakeByRefType() : declaring);
                parameters.Add(type + " __this");
            }
            for (var i = 0; i < d.ParameterCount; i++)
            {
                var parameter = Package.Params[d.ParameterStart + i];
                var parameterName = ZzzHeaderWriter.FieldName(method.DeclaredParameters[i].Name);
                if (string.IsNullOrEmpty(parameterName))
                {
                    parameterName = $"arg{i}";
                }

                while (!names.Add(parameterName))
                {
                    parameterName += "_" + i;
                }

                parameters.Add(CType(ResolveType(parameter.TypeIndex, classArgs, methodArgs)) + " " + parameterName);
            }
            parameters.Add("const MethodInfo * method");
            var returnType = CType(ResolveType(d.ReturnType, classArgs, methodArgs));
            return (name, returnType, string.Join(", ", parameters), complete);
        }

        internal void WriteCppFunctions(TextWriter output)
        {
            void FunctionPointer(int definition, int spec, ulong address)
            {
                var signature = NativeSignature(definition, spec);
                if (signature.Complete)
                {
                    output.WriteLine($"DO_APP_FUNC(0x{address - Package.BinaryImage.ImageBase:X8}, {signature.ReturnType}, {signature.Name}, ({signature.Parameters}));");
                }
                else
                {
                    output.WriteLine($"// {signature.Name}: unresolved value storage; address 0x{address - Package.BinaryImage.ImageBase:X8}.");
                }
            }
            for (var i = 0; i < Package.Methods.Length; i++)
            {
                if (model.TypeModel.MethodsByDefinitionIndex[i].VirtualAddress is { } address)
                {
                    FunctionPointer(i, -1, address.Start);
                }
            }

            for (var i = 0; i < Package.MethodSpecs.Length; i++)
            {
                if (Package.GenericMethodPointers.TryGetValue(Package.MethodSpecs[i], out var address))
                {
                    FunctionPointer(Package.MethodSpecs[i].MethodDefinitionIndex, i, address);
                }
            }

            foreach (var usage in Package.MetadataUsages.Where(u => u.Type is MetadataUsageType.MethodDef or MetadataUsageType.MethodRef))
            {
                var spec = usage.Type == MetadataUsageType.MethodRef ? usage.SourceIndex : -1;
                var definition = spec >= 0 ? Package.MethodSpecs[spec].MethodDefinitionIndex : usage.SourceIndex;
                output.WriteLine($"DO_APP_FUNC_METHODINFO(0x{usage.VirtualAddress - Package.BinaryImage.ImageBase:X8}, {MethodName(definition, spec)}__MethodInfo_{usage.VirtualAddress:X});");
            }
        }

        private string DotNetSignature(int definition, int specIndex)
        {
            var method = model.TypeModel.MethodsByDefinitionIndex[definition];
            if (specIndex < 0)
            {
                return method.ToString();
            }

            var d = Package.Methods[definition];
            var spec = Package.MethodSpecs[specIndex];
            var classArgs = Arguments(spec.ClassIndexIndex);
            var methodArgs = Arguments(spec.MethodIndexIndex);
            var parameters = new string[d.ParameterCount];
            for (var i = 0; i < parameters.Length; i++)
            {
                var type = ResolveType(Package.Params[d.ParameterStart + i].TypeIndex, classArgs, methodArgs);
                parameters[i] = type.IsByRef ? type.Name.TrimEnd('&') + " ByRef" : type.Name;
            }
            var declaring = method.DeclaringType;
            if (classArgs.Length > 0)
            {
                declaring = declaring.MakeGenericType(classArgs.Select(a => model.TypeModel.TypesByReferenceIndex[a]).ToArray());
            }

            var genericArguments = methodArgs.Length == 0 ? method.GetFullTypeParametersString() : "[" + string.Join(",", methodArgs.Select(a => model.TypeModel.TypesByReferenceIndex[a].Name)) + "]";
            var name = method is ConstructorInfo ? declaring.Name + genericArguments : ResolveType(d.ReturnType, classArgs, methodArgs).Name + " " + method.Name + genericArguments;
            return name + "(" + string.Join(", ", parameters) + ")";
        }

        private int[] Arguments(int instance) => Native.Arguments(instance);

        private TypeInfo ResolveType(int index, int[] classArgs, int[] methodArgs) => Native.ResolveType(index, classArgs, methodArgs);

        private void Array(string name, Action body)
        {
            writer.WriteStartArray(name);
            if (allowComments)
            {
                writer.WriteCommentValue(" " + name + " ");
            }

            if (!SupplementDebugInfo || !JSONMetadata.IsDebugSymbolSection(name))
                body();
            writer.WriteEndArray();
        }

        private void Object(Action body)
        {
            writer.WriteStartObject();
            body();
            writer.WriteEndObject();
            if (writer.BytesPending > 1 << 20)
            {
                writer.Flush();
            }
        }

        private void Name(ulong address, string name)
        {
            writer.WriteString("virtualAddress", address.ToAddressString());
            writer.WriteString("name", name);
        }

        private void Typed(ulong address, string name, string type)
        {
            Name(address, name);
            writer.WriteString("type", type);
        }

        private void Function(ulong address, string name, string signature)
        {
            Name(address, name);
            writer.WriteString("signature", signature);
        }
    }
}
