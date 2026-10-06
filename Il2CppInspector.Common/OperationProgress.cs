namespace Il2CppInspector
{
    // Negative totals indicate work without a measurable item count.
    public readonly record struct OperationProgress(string Label, long Current, long Total, string Detail = null);
}
