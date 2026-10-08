using Il2CppInspector.Cpp;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Model
{
    internal abstract class NativeTypeModel(TypeModel model) : Plugins.GameNativeModel
    {
        internal readonly TypeModel Model = model;
        internal Il2CppInspector Package => Model.Package;
        public override Dictionary<string, TypeInfo> ArrayTypes { get; } = [];
        private CppNamespace.Namer<TypeInfo> typeNamer;

        internal string ReferenceName(TypeInfo type) => typeNamer.GetName(type);

        internal string InterfaceVTableName(TypeInfo type) => Name(Model.TypesByDefinitionIndex[type.Index]) + "__InterfaceVTable";

        internal string VTableName(TypeInfo type, bool split) => Name(Model.TypesByDefinitionIndex[type.Index]) + "__VTable_" + (split ? "Dual" : "Single");

        internal void InitializeNaming(AppModel app)
        {
            // Share the stock namer, including its keyword and runtime-header reservations.
            typeNamer = app.NativeTypeNamer;
            // Keep names stable regardless of which export first requests a declaration.
            foreach (var type in Model.TypesByDefinitionIndex.Where(t => t != null).Concat(GenericTypes.OrderBy(pair => pair.Key).Select(pair => pair.Value)))
                Name(type);
            foreach (var type in Model.TypesByReferenceIndex)
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
            if (type.IsGenericType && !type.IsGenericTypeDefinition && !GenericOrdinals.ContainsKey(type))
            {
                throw new InvalidOperationException("A constructed type without a recorded layout has no emitted native declaration.");
            }

            return ReferenceName(type);
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

            var primitive = CppDeclarationGenerator.PrimitiveTypeName(type);
            if (primitive != null)
            {
                return primitive;
            }

            if (type.IsGenericType && !GenericOrdinals.ContainsKey(type))
            {
                return "void *";
            }

            if (type.IsGenericType && type.IsValueType && !HasInstanceLayout(type))
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
            if (element.IsValueType && element.IsGenericType && CType(element) == "void *")
            {
                return null;
            }

            bool CanNameStorage(TypeInfo storage)
            {
                if (storage.IsArray)
                {
                    return ArrayName(storage) != null;
                }

                if (storage.IsPointer)
                {
                    return CanNameStorage(storage.ElementType);
                }

                if (storage.IsGenericType && !GenericOrdinals.ContainsKey(storage))
                {
                    return storage.IsEnum;
                }

                return true;
            }
            if (!CanNameStorage(element))
            {
                return null;
            }

            var name = ReferenceName(type);
            ArrayTypes.TryAdd(name, type);
            return name;
        }

        internal virtual bool HasInstanceLayout(TypeInfo type) => !type.IsGenericType || type.IsGenericTypeDefinition || type.GameLayoutGroup.HasValue;

        internal abstract int Alignment(TypeInfo type);
        internal abstract CppComplexType CreateClass(NativeLayoutAnalysisModel layouts, TypeInfo type);
    }
}
