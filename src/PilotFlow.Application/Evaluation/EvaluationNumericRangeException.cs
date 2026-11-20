namespace PilotFlow.Application.Evaluation;

public sealed class EvaluationNumericRangeException(string path, Guid? entityId)
    : Exception("A calculated value exceeds the numeric range of the API.")
{
    public string Path { get; } = path;
    public Guid? EntityId { get; } = entityId;
}
