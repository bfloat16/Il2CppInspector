// Copyright (c) 2020-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty
// All rights reserved

using System.Text.Json;
using Il2CppInspector.Cpp;
using Il2CppInspector.Model;
using Il2CppInspector.Next;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Outputs
{
    // Output module to produce machine readable metadata in JSON format
    public class JSONMetadata
    {
        private readonly AppModel model;
        private Utf8JsonWriter writer;

        // Allow non-compliant C-style comments in JSON output
        public bool AllowComments { get; set; } = false;
        public bool SupplementDebugInfo { get; set; }

        internal static bool IsDebugSymbolSection(string name) =>
            name
                is "methodDefinitions"
                    or "constructedGenericMethods"
                    or "customAttributesGenerators"
                    or "methodInvokers"
                    or "functionAddresses"
                    or "functionMetadata"
                    or "apis"
                    or "exports"
                    or "symbols";

        public JSONMetadata(AppModel model) => this.model = model;

        // Write JSON metadata to file
        public void Write(string outputFile)
        {
            using var fs = new FileStream(outputFile, FileMode.Create);
            writer = new Utf8JsonWriter(fs, options: new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();

            // Output address map of everything in the binary that we recognize
            writeObject(
                "addressMap",
                () =>
                {
                    writeMethods();
                    writeStringLiterals();
                    writeUsages();
                    writeFunctions();
                    writeMetadata();
                    writeApis();
                    writeExports();
                    writeSymbols();
                    writeFields();
                    writeAdditionalUsages();
                },
                "Address map of methods, internal functions, type pointers and string literals in the binary file"
            );

            writer.WriteEndObject();
            writer.Dispose();
        }

        private void writeMethods()
        {
            writeArray("methodDefinitions", () => writeMethods(model.GetMethodGroup("types_from_methods")), "Method definitions");
            writeArray("constructedGenericMethods", () => writeMethods(model.GetMethodGroup("types_from_generic_methods")), "Constructed generic methods");

            // We could use ILModel.CustomAttributeGenerators here to ensure uniqueness but then we lose function name information
            // TODO: Merge CAG names that deal with multiple attribute types to reflect all of the attribute type names (solving the above)
            writeArray(
                "customAttributesGenerators",
                () =>
                {
                    foreach (var method in model.TypeModel.AttributesByIndices.Values)
                    {
                        writeObject(() => writeTypedFunctionName(method.VirtualAddress.Start, method.Signature, method.Name));
                    }
                },
                "Custom attributes generators"
            );

            writeArray(
                "methodInvokers",
                () =>
                {
                    foreach (var method in model.EnumerateNativeSupportMethods().Where(m => m.Name.StartsWith("Il2CppInvoker_", StringComparison.Ordinal)))
                        writeObject(() => writeTypedFunctionName(method.Address, method.Signature.ToSignatureString(), method.Name));
                },
                "Method.Invoke thunks"
            );
        }

        private void writeMethods(IEnumerable<AppMethod> methods)
        {
            foreach (var method in methods.Where(m => m.HasCompiledCode))
            {
                writeObject(() =>
                {
                    writeTypedFunctionName(method.MethodCodeAddress, method.CppFnPtrType.ToSignatureString(), method.ToMangledString());
                    writeDotNetSignature(method.Method);
                    if (model.GameNativeModel != null)
                        writer.WriteBoolean("signatureComplete", method.SignatureComplete);

                    var groupString = $"{method.Method.DeclaringType.Assembly.ShortName}/{method.Method.DeclaringType.FullName.Replace(".", "/")}";
                    writer.WriteString("group", groupString);
                });
            }
        }

        private void writeStringLiterals()
        {
            writeArray(
                "stringLiterals",
                () =>
                {
                    foreach (var str in model.Strings)
                        writeObject(() =>
                        {
                            // For version < 19
                            if (model.StringIndexesAreOrdinals)
                            {
                                writer.WriteNumber("ordinal", str.Key);
                                writer.WriteString("name", $"STRINGLITERAL_{str.Key}_{stringToIdentifier(str.Value)}");
                                // For version >= 19
                            }
                            else
                            {
                                writer.WriteString("virtualAddress", str.Key.ToAddressString());
                                writer.WriteString("name", "StringLiteral_" + stringToIdentifier(str.Value));
                            }
                            writer.WriteString("string", str.Value);
                        });
                },
                "String literals"
            );
        }

        private void writeUsages()
        {
            // TypeInfo addresses for all types from metadata usages
            writeArray(
                "typeInfoPointers",
                () =>
                {
                    foreach (var usage in (model.Package.MetadataUsages ?? []).Where(u => u.Type == MetadataUsageType.TypeInfo))
                    {
                        var type = model.TypeModel.GetMetadataUsageType(usage);
                        var className = model.GameNativeModel?.ClassName(type) ?? model.Types[type].Name + "__Class";
                        writeObject(() =>
                        {
                            writeTypedName(usage.VirtualAddress, $"struct {className} *", MangledNameBuilder.TypeInfo(type));
                            writeDotNetTypeName(type);
                        });
                    }
                },
                "Il2CppClass (TypeInfo) pointers"
            );

            // Reference addresses for all types from metadata usages
            writeArray(
                "typeRefPointers",
                () =>
                {
                    foreach (var usage in (model.Package.MetadataUsages ?? []).Where(u => u.Type == MetadataUsageType.Type))
                    {
                        var type = model.TypeModel.GetMetadataUsageType(usage);
                        writeObject(() =>
                        {
                            writeName(usage.VirtualAddress, MangledNameBuilder.TypeRef(type));
                            writeDotNetTypeName(type);
                        });
                    }
                },
                "Il2CppType (TypeRef) pointers"
            );

            // Metedata usage methods
            writeArray(
                "methodInfoPointers",
                () =>
                {
                    foreach (var usage in (model.Package.MetadataUsages ?? []).Where(u => u.Type is MetadataUsageType.MethodDef or MetadataUsageType.MethodRef))
                    {
                        var method = model.TypeModel.GetMetadataUsageMethod(usage);
                        writeObject(() =>
                        {
                            writeName(usage.VirtualAddress, MangledNameBuilder.MethodInfo(method));
                            writeDotNetSignature(method);
                            if (method.VirtualAddress.HasValue)
                                writer.WriteString("methodAddress", method.VirtualAddress.Value.Start.ToAddressString());
                        });
                    }
                },
                "MethodInfo pointers"
            );
        }

        private void writeFunctions()
        {
            writeArray(
                "functionAddresses",
                () =>
                {
                    foreach (var func in model.Package.FunctionAddresses)
                        writer.WriteStringValue(func.Key.ToAddressString());
                },
                "Function boundaries"
            );
        }

        private void writeMetadata()
        {
            var binary = model.Package.Binary;

            writeArray(
                "typeMetadata",
                () =>
                {
                    writeObject(() => writeTypedName(binary.CodeRegistrationPointer, "struct Il2CppCodeRegistration", "g_CodeRegistration"));
                    writeObject(() => writeTypedName(binary.MetadataRegistrationPointer, "struct Il2CppMetadataRegistration", "g_MetadataRegistration"));

                    foreach (var ptr in binary.CodeGenModulePointers)
                        writeObject(() => writeTypedName(ptr.Value, "struct Il2CppCodeGenModule", $"g_{ptr.Key.Replace(".dll", "")}CodeGenModule"));
                },
                "IL2CPP Type Metadata"
            );

            // plagiarism. noun - https://www.lexico.com/en/definition/plagiarism
            //   the practice of taking someone else's work or ideas and passing them off as one's own.
            // Synonyms: copying, piracy, theft, strealing, infringement of copyright

            writeArray(
                "functionMetadata",
                () =>
                {
                    // This will be zero if we found the structs from the symbol table
                    if (binary.RegistrationFunctionPointer != 0)
                    {
                        var signature = model.GetRegistrationSignature();
                        writeObject(() => writeTypedFunctionName(binary.RegistrationFunctionPointer, signature.ToSignatureString(), signature.Name));
                    }
                },
                "IL2CPP Function Metadata"
            );

            // TODO: In the future, add data ranges for the entire IL2CPP metadata tree
            writeArray(
                "arrayMetadata",
                () =>
                {
                    if (model.Package.Version >= MetadataVersions.V242)
                    {
                        writeObject(() => writeTypedArray(binary.CodeRegistration.CodeGenModules, binary.Modules.Count, "struct Il2CppCodeGenModule *", "g_CodeGenModules"));
                    }
                },
                "IL2CPP Array Metadata"
            );
        }

        private void writeApis()
        {
            var apis = model.AvailableAPIs;
            writeArray(
                "apis",
                () =>
                {
                    foreach (var api in apis)
                    {
                        var address = apis.primaryToSubkeyMapping[api.Key];

                        writeObject(() => writeTypedFunctionName(address, api.Value.ToSignatureString(), api.Key));
                    }
                },
                "IL2CPP API functions"
            );
        }

        private void writeExports()
        {
            var exports = model.Exports;

            writeArray(
                "exports",
                () =>
                {
                    foreach (var export in exports)
                    {
                        writeObject(() => writeName(export.VirtualAddress, export.Name));
                    }
                },
                "Exports"
            );
        }

        private void writeSymbols()
        {
            var symbols = model.Symbols.Values;

            writeArray(
                "symbols",
                () =>
                {
                    foreach (var symbol in symbols)
                    {
                        writeObject(() =>
                        {
                            writeName(symbol.VirtualAddress, symbol.Name);
                            writer.WriteString("type", symbol.Type.ToString());
                        });
                    }
                },
                "Symbol table"
            );
        }

        private void writeFields()
        {
            writeArray(
                "fields",
                () =>
                {
                    foreach (var (addr, field) in model.Fields)
                        writeFieldObject(addr, (field.Field + "_Field").ToCIdentifier(), field.Value, field.Field);
                }
            );

            writeArray(
                "fieldRvas",
                () =>
                {
                    foreach (var (addr, rva) in model.FieldRvas)
                        writeFieldObject(addr, (rva.Field + "_FieldRva").ToCIdentifier(), rva.Value, rva.Field);
                }
            );
        }

        private void writeFieldObject(ulong addr, string name, string value, FieldInfo field)
        {
            writeObject(() =>
            {
                writer.WriteString("virtualAddress", addr.ToAddressString());
                writer.WriteString("name", name);
                writer.WriteString("value", value);
                if (model.Package.Metadata.HasGameAdapter)
                {
                    writer.WriteNumber("storageTag", field.StorageTag);
                    writer.WriteNumber("offset", field.Offset);
                    writer.WriteString("storageBase", model.Package.Metadata.GameAdapter.StorageBase((uint)field.StorageTag));
                }
            });
        }

        private void writeAdditionalUsages()
        {
            var additional = model.Package.Metadata.GameAdapter?.AdditionalUsages;
            if (additional == null)
                return;
            foreach (var section in additional.GroupBy(u => u.Section))
                writeArray(
                    section.Key,
                    () =>
                    {
                        foreach (var usage in section)
                            writeObject(() =>
                            {
                                writeTypedName(usage.Address, usage.Type, usage.Name);
                                writeDotNetTypeName(model.TypeModel.TypesByReferenceIndex[usage.TypeIndex]);
                            });
                    }
                );
        }

        // JSON helpers
        private void writeObject(Action objectWriter) => writeObject(null, objectWriter);

        private void writeObject(string name, Action objectWriter, string description = null)
        {
            if (AllowComments && description != null)
                writer.WriteCommentValue(" " + description + " ");
            if (name != null)
                writer.WriteStartObject(name);
            else
                writer.WriteStartObject();
            objectWriter();
            writer.WriteEndObject();
        }

        private void writeArray(string name, Action arrayWriter, string description = null)
        {
            writer.WriteStartArray(name);
            if (AllowComments && description != null)
                writer.WriteCommentValue(" " + description + " ");
            if (!SupplementDebugInfo || !IsDebugSymbolSection(name))
                arrayWriter();
            writer.WriteEndArray();
        }

        private void writeName(ulong address, string name)
        {
            writer.WriteString("virtualAddress", address.ToAddressString());
            writer.WriteString("name", name.ToEscapedString());
        }

        private void writeTypedName(ulong address, string type, string name)
        {
            writeName(address, name);
            writer.WriteString("type", type.ToEscapedString());
        }

        private void writeTypedFunctionName(ulong address, string type, string name)
        {
            writeName(address, name);
            writer.WriteString("signature", type.ToEscapedString());
        }

        private void writeTypedArray(ulong address, int count, string type, string name)
        {
            writeTypedName(address, type, name);
            writer.WriteNumber("count", count);
        }

        private void writeDotNetSignature(MethodBase method)
        {
            writer.WriteString("dotNetSignature", method.ToString().ToEscapedString());
        }

        private void writeDotNetTypeName(TypeInfo type)
        {
            writer.WriteString("dotNetType", type.CSharpName);
        }

        private static string stringToIdentifier(string str)
        {
            str = str.Substring(0, Math.Min(32, str.Length));
            return str.ToCIdentifier();
        }
    }
}
