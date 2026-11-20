using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PilotFlow.Application.Decisions;

namespace PilotFlow.Infrastructure.Persistence.Decisions;

internal static class SnapshotJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new DecimalConverter(), new UtcInstantConverter() },
    };

    internal static JsonElement Serialize(VersionSnapshot snapshot) => JsonSerializer.SerializeToElement(snapshot, Options);

    internal static VersionSnapshot Deserialize(JsonElement json) =>
        json.Deserialize<VersionSnapshot>(Options) ?? throw new InvalidDataException("Invalid decision snapshot.");

    internal static string Hash(JsonElement json)
    {
        var canonical = new StringBuilder();
        AppendCanonical(canonical, json);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendCanonical(StringBuilder destination, JsonElement json)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Object:
                destination.Append('{');
                var first = true;
                foreach (var property in json.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        destination.Append(',');
                    }
                    first = false;
                    AppendString(destination, property.Name);
                    destination.Append(':');
                    AppendCanonical(destination, property.Value);
                }
                destination.Append('}');
                break;
            case JsonValueKind.Array:
                destination.Append('[');
                var index = 0;
                foreach (var item in json.EnumerateArray())
                {
                    if (index++ > 0)
                    {
                        destination.Append(',');
                    }
                    AppendCanonical(destination, item);
                }
                destination.Append(']');
                break;
            case JsonValueKind.String:
                AppendString(destination, json.GetString()!);
                break;
            case JsonValueKind.Number:
                // The snapshot schema uses JSON numbers only for integer indices, counts, ranks and revisions.
                destination.Append(json.GetInt64().ToString(CultureInfo.InvariantCulture));
                break;
            default:
                destination.Append(json.GetRawText());
                break;
        }
    }

    private static void AppendString(StringBuilder destination, string value)
    {
        destination.Append('"');
        foreach (var character in value)
        {
            var escape = character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                < ' ' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                _ => null,
            };
            if (escape is null)
            {
                destination.Append(character);
            }
            else
            {
                destination.Append(escape);
            }
        }
        destination.Append('"');
    }

    private sealed class DecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("0.######", CultureInfo.InvariantCulture));
    }

    private sealed class UtcInstantConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTimeOffset();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime);
    }
}
