using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PilotFlow.Api.Controllers;

internal sealed partial class DecimalStringConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && reader.GetString() is { } value
            && DecimalPattern().IsMatch(value)
            && decimal.TryParse(value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        throw new JsonException("Expected a nonnegative decimal string, with up to 18 whole and 6 fractional digits.");
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));

    [GeneratedRegex(@"\A(?:0|[1-9][0-9]{0,17})(?:\.[0-9]{1,6})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPattern();
}
