using System.Text.Json;
using Shouldly;
using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Api.Tests.Extracts;

public sealed class MonthDateOnlyJsonConverterTests
{
    [Fact]
    public void Parses_year_month_string_anchored_to_day_one()
    {
        var json = """{"month":"2026-03","return_pct":0.012,"class_id":"C000111"}""";

        var parsed = JsonSerializer.Deserialize<MonthlyReturnExtract>(json);

        parsed.ShouldNotBeNull();
        parsed.Month.ShouldBe(new DateOnly(2026, 3, 1));
        parsed.ReturnPct.ShouldBe(0.012m);
    }

    [Fact]
    public void Still_accepts_full_iso_date()
    {
        var json = """{"month":"2026-03-15","return_pct":0.012,"class_id":"C000111"}""";

        var parsed = JsonSerializer.Deserialize<MonthlyReturnExtract>(json);

        parsed.ShouldNotBeNull();
        parsed.Month.ShouldBe(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void Throws_on_unrecognized_format()
    {
        var json = """{"month":"March 2026","return_pct":0.012,"class_id":"C000111"}""";

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<MonthlyReturnExtract>(json));
    }
}
