namespace Il2CppInspector.Tests.Common.Cpp
{
    internal static class CppTypeTests
    {
        internal static void Run()
        {
            var unionTypes = new CppTypeCollection(64);
            var unionA = unionTypes.Struct("A");
            unionA.AddField("a", unionTypes.GetType("int32_t"));
            var unionB = unionTypes.Struct("B");
            unionB.AddField("b", unionTypes.GetType("int32_t"));
            var union = unionTypes.Union("U");
            union.AddField("first", unionA);
            union.AddField("second", unionB);
            Check(union.Flattened["a"].OffsetBytes == 0 && union.Flattened["b"].OffsetBytes == 0, "Overlapping nested C++ layouts retain both fields when flattened");
        }
    }
}
