using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PilotFlow.Domain.Analyses;

namespace PilotFlow.Infrastructure.Persistence;

internal static class StoredJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { AllowLegacyEvidence },
        },
    };

    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    internal static T Read<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidDataException("Invalid null metadata in PostgreSQL.");

    private static void AllowLegacyEvidence(JsonTypeInfo type)
    {
        // Older rows can omit nullable evidence fields. Require them on HTTP input only.
        if (type.Type == typeof(AnalysisEvidence))
        {
            foreach (var field in type.Properties)
            {
                field.IsRequired = false;
            }
        }
    }
}
