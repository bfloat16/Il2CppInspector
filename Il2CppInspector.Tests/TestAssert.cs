namespace Il2CppInspector.Tests
{
    internal static class TestAssert
    {
        internal static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }

            Pass(message);
        }

        internal static void Pass(string message) => Console.WriteLine("PASS " + message);
    }
}
