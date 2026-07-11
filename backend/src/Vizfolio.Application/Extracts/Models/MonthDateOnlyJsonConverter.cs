using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vizfolio.Application.Extracts.Models;

internal sealed class MonthDateOnlyJsonConverter : JsonConverter<DateOnly>
{
    private static readonly string[] Formats = ["yyyy-MM", "yyyy-MM-dd"];

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException("Expected a non-empty month string.");

        if (!DateOnly.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new JsonException($"Could not parse '{value}' as a month (expected yyyy-MM or yyyy-MM-dd).");

        return new DateOnly(parsed.Year, parsed.Month, 1);
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("yyyy-MM", CultureInfo.InvariantCulture));
}
