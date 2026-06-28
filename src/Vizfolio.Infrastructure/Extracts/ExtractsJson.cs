using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vizfolio.Infrastructure.Extracts;

internal static class ExtractsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
