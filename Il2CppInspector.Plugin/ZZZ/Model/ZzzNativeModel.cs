using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model
{
    internal sealed class ZzzNativeModel : Plugins.GameNativeModel
    {
        internal readonly TypeModel Model;
        internal Il2CppInspector Package => Model.Package;
        public override Dictionary<TypeInfo, int> GenericOrdinals { get; }
        public override Dictionary<int, TypeInfo> GenericTypes { get; }
        public override Dictionary<string, TypeInfo> ArrayTypes { get; } = [];
        private readonly Dictionary<int, int[]> instanceArguments = [];

        internal string MethodName(int definition, int spec)
        {
            var method = Model.MethodsByDefinitionIndex[definition];
            return method.DeclaringType.FullName.ToCIdentifier() + "_" + method.Name.ToCIdentifier() + $"_{definition}" + (spec >= 0 ? $"_Generic_{spec}" : "");
        }

        internal int[] Arguments(int instance)
        {
            if (instance < 0)
                return [];
            if (!instanceArguments.TryGetValue(instance, out var args))
            {
                var inst = Package.GenericInstances[instance];
                args = Package.BinaryImage.ReadMappedUWordArray(inst.TypeArgv, (int)inst.TypeArgc).Select(a => Package.TypeReferenceIndicesByAddress[a]).ToArray();
                instanceArguments.Add(instance, args);
            }
            return args;
        }

        internal TypeInfo ResolveType(int index, int[] classArgs, int[] methodArgs, int depth = 0)
        {
            if (depth > 64)
                throw new InvalidDataException("Cyclic MORAX method signature.");
            var raw = Package.TypeReferences[index];
            TypeInfo result;
            switch (raw.Type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    var args = raw.Type == Il2CppTypeEnum.IL2CPP_TYPE_VAR ? classArgs : methodArgs;
                    var number = Package.GenericParameters[raw.Data.GenericParameterIndex].Num;
                    result = number < args.Length ? ResolveType(args[number], [], [], depth + 1) : Model.TypesByReferenceIndex[index];
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    var pair = ((ZzzMorax)Package.Metadata.GameAdapter).GenericClass(raw.Data.Value);
                    result = Model.TypesByDefinitionIndex[pair.Definition].MakeGenericType(Arguments(pair.Instance).Select(a => ResolveType(a, classArgs, methodArgs, depth + 1)).ToArray());
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    var element = ResolveType(Package.TypeReferenceIndicesByAddress[raw.Data.Value], classArgs, methodArgs, depth + 1);
                    result = raw.Type == Il2CppTypeEnum.IL2CPP_TYPE_PTR ? element.MakePointerType() : element.MakeArrayType(1);
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    var array = Package.BinaryImage.ReadMappedVersionedObject<Il2CppArrayType>(raw.Data.ArrayType);
                    result = ResolveType(Package.TypeReferenceIndicesByAddress[array.ElementType], classArgs, methodArgs, depth + 1).MakeArrayType(array.Rank);
                    break;
                default:
                    return Model.TypesByReferenceIndex[index];
            }
            return raw.ByRef && !result.IsByRef ? result.MakeByRefType() : result;
        }

        internal ZzzNativeModel(TypeModel model)
        {
            Model = model;
            var layouts = (ZzzSharedTypeLayouts)model.GameLayouts;
            GenericOrdinals = layouts.GenericOrdinals;
            GenericTypes = layouts.GenericTypes;
            foreach (var type in model.TypesByReferenceIndex)
            {
                var element = type;
                while (element is { HasElementType: true })
                {
                    if (element.IsArray)
                    {
                        ArrayName(element);
                        break;
                    }
                    element = element.ElementType;
                }
            }
        }

        public override string Name(TypeInfo type)
        {
            if (GenericOrdinals.TryGetValue(type, out var ordinal))
            {
                return $"Zzz_Generic_{ordinal}";
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                throw new InvalidOperationException("A ZZZ constructed type without a recorded ordinal has no emitted native declaration.");
            }

            return $"Zzz_Type_{type.Index}";
        }

        public override string ClassName(TypeInfo type) =>
            type == null || type.HasElementType || type.ContainsGenericParameters || (type.IsGenericType && !type.IsGenericTypeDefinition && !GenericOrdinals.ContainsKey(type))
                ? "Il2CppClass"
                : Name(type) + "__Class";

        public override string CType(TypeInfo type)
        {
            if (type == null)
            {
                return "void *";
            }

            if (type.HasElementType)
            {
                if (type.IsArray)
                {
                    return ArrayName(type) is { } arrayName ? "struct " + arrayName + " *" : "Il2CppArray *";
                }

                var element = type.ElementType;
                var storage = CType(element);
                if (element.ContainsGenericParameters)
                {
                    return "void *";
                }
                // An opaque value is storage, not a pointer slot. A pointer to it
                // remains one level of indirection; IntPtr& still needs void **.
                if (element.IsGenericType && element.IsValueType && !element.IsEnum && storage == "void *")
                {
                    return GenericOrdinals.ContainsKey(element) ? "struct " + Name(element) + " *" : "void *";
                }

                return storage + " *";
            }
            if (type.IsEnum)
            {
                return !type.IsGenericType || type.IsGenericTypeDefinition || GenericOrdinals.ContainsKey(type) ? Name(type) : CType(type.GetEnumUnderlyingType());
            }

            if (type.ContainsGenericParameters)
            {
                return "void *";
            }

            var primitive = type.FullName switch
            {
                "System.Void" => "void",
                "System.Boolean" => "bool",
                "System.Byte" => "uint8_t",
                "System.SByte" => "int8_t",
                "System.Int16" => "int16_t",
                "System.UInt16" or "System.Char" => "uint16_t",
                "System.Int32" => "int32_t",
                "System.UInt32" => "uint32_t",
                "System.Int64" => "int64_t",
                "System.UInt64" => "uint64_t",
                "System.Single" => "float",
                "System.Double" => "double",
                "System.IntPtr" or "System.UIntPtr" => "void *",
                _ => null,
            };
            if (primitive != null)
            {
                return primitive;
            }

            if (type.IsGenericType && !GenericOrdinals.ContainsKey(type))
            {
                return "void *";
            }

            if (type.IsGenericType && type.IsValueType && !type.IsGenericTypeDefinition && !type.GameLayoutGroup.HasValue)
            {
                return "void *";
            }

            return "struct " + Name(type) + (type.IsValueType ? "" : " *");
        }

        public override bool IsSignatureTypeComplete(TypeInfo type)
        {
            if (type == null)
            {
                return false;
            }

            if (type.HasElementType || type.IsEnum)
            {
                return true;
            }

            if (type.ContainsGenericParameters)
            {
                return false;
            }

            return !type.IsValueType || !type.IsGenericType || CType(type) != "void *";
        }

        public override string ArrayName(TypeInfo type)
        {
            if (!type.IsArray || type.ContainsGenericParameters)
            {
                return null;
            }

            var element = type.ElementType;
            // A missing value layout cannot be represented by a pointer-sized array element.
            if (element.IsValueType && CType(element) == "void *")
            {
                return null;
            }

            string StorageName(TypeInfo storage)
            {
                if (storage.IsArray)
                {
                    return ArrayName(storage);
                }

                if (storage.IsPointer)
                {
                    return StorageName(storage.ElementType) is { } pointed ? "Pointer_" + pointed : null;
                }

                if (storage.IsGenericType && !GenericOrdinals.ContainsKey(storage))
                {
                    return storage.IsEnum ? $"Zzz_Enum_{storage.Index}" : null;
                }

                return Name(storage);
            }
            if (StorageName(element) is not { } suffix)
            {
                return null;
            }

            var name = $"Zzz_Array_{type.GetArrayRank()}_{suffix}";
            ArrayTypes.TryAdd(name, type);
            return name;
        }
    }
}
