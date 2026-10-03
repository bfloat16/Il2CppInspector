using Il2CppInspector.Cpp;

namespace Il2CppInspector.Model;

public sealed record NativeMethod(string Name, ulong Address, CppFnPtrType Signature, bool SignatureComplete = true);
