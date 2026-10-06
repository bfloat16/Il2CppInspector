/*
    Copyright 2020-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty
    All rights reserved.
*/

using Il2CppInspector.Cpp;
using Il2CppInspector.Cpp.UnityHeaders;

namespace Il2CppInspector.Reflection
{
    // Class representing a MethodBase.Invoke() method for a specific signature
    // Every IL2CPP invoker has the signature:
    // void* RuntimeInvoker_{RequiresObject (True/False)}{ReturnType}_{ParameterTypes}
    // (Il2CppMethodPointer pointer, const RuntimeMethod* methodMetadata, void* obj, void** args)
    public class MethodInvoker
    {
        // IL2CPP invoker index
        public int Index { get; }

        // IL2CPP package
        public Il2CppInspector Package { get; }

        // Virtual address of the invoker function
        public (ulong Start, ulong End) VirtualAddress { get; }

        // If false, the first argument to the called function pointers must be the object instance
        public bool IsStatic { get; }

        // Return type
        public TypeInfo ReturnType { get; }

        // Argument types
        public TypeInfo[] ParameterTypes { get; }

        // Find the correct method invoker for a method with a specific signature
        public MethodInvoker(MethodBase exampleMethod, int index)
        {
            var model = exampleMethod.Assembly.Model;
            Package = exampleMethod.Assembly.Model.Package;

            Index = index;
            IsStatic = exampleMethod.IsStatic;

            ReturnType = exampleMethod.IsConstructor ? model.TypesByFullName["System.Void"] : mapParameterType(model, ((MethodInfo)exampleMethod).ReturnType);
            ParameterTypes = exampleMethod.DeclaredParameters.Select(p => mapParameterType(model, p.ParameterType)).ToArray();

            var start = Package.MethodInvokePointers[Index];
            VirtualAddress = (start, Package.FunctionAddresses[start]);
        }

        // The invokers use Object for all reference types, and SByte for booleans
        private TypeInfo mapParameterType(TypeModel model, TypeInfo type) =>
            type switch
            {
                { IsValueType: false } => model.TypesByFullName["System.Object"],
                { FullName: "System.Boolean" } => model.TypesByFullName["System.SByte"],
                _ => type,
            };

        // Get the machine code of the C++ function
        public byte[] GetMethodBody() => Package.BinaryImage.ReadMappedBytes(VirtualAddress.Start, (int)(VirtualAddress.End - VirtualAddress.Start));

        public string Name => $"RuntimeInvoker_{!IsStatic}{ReturnType.BaseName.ToCIdentifier()}_" + string.Join("_", ParameterTypes.Select(p => p.BaseName.ToCIdentifier()));

        // Display as a C++ method signature; MethodInfo* is the same as RuntimeMethod* (see codegen/il2cpp-codegen-metadata.h)
        public string GetSignature(UnityVersion version) => Signature(version, Name);

        private static string Signature(UnityVersion version, string declarator) =>
            version.CompareTo("2021.1.0") >= 0 ? $"void {declarator}(Il2CppMethodPointer pointer, const MethodInfo* methodMetadata, void* obj, void** args, void* ret)"
            : version.CompareTo("2017.1.0") >= 0 ? $"void* {declarator}(Il2CppMethodPointer pointer, const MethodInfo* methodMetadata, void* obj, void** args)"
            : $"void* {declarator}(const MethodInfo* method, void* obj, void** args)";

        internal static CppFnPtrType CreateSignature(CppTypeCollection types, UnityVersion version, string name) => CppFnPtrType.FromSignature(types, Signature(version, $"(*{name})"));

        public override string ToString() => GetSignature(new UnityVersion("2017.1.0"));
    }
}
