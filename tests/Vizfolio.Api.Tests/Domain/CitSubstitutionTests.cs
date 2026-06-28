using Shouldly;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Api.Tests.Domain;

public sealed class CitSubstitutionTests
{
    private static CitSubstitution CreateSubstitution() =>
        new("voo", "Vanguard S&P 500 ETF", CitSubstitutionFidelity.High, "  Same index, <1% drift.  ");

    [Theory]
    [InlineData("", "name")]
    [InlineData("   ", "name")]
    [InlineData("VOO", "")]
    [InlineData("VOO", "   ")]
    public void Constructor_requires_substitute_ticker_and_name(string ticker, string name)
    {
        Should.Throw<ArgumentException>(() =>
            new CitSubstitution(ticker, name, CitSubstitutionFidelity.High, null));
    }

    [Fact]
    public void Constructor_uppercases_and_trims_ticker()
    {
        var substitution = CreateSubstitution();

        substitution.SubstituteTicker.ShouldBe("VOO");
    }

    [Fact]
    public void Constructor_trims_note_and_normalizes_blank_to_null()
    {
        var withNote = CreateSubstitution();
        var withoutNote = new CitSubstitution("BND", "Vanguard Bond", CitSubstitutionFidelity.Medium, "   ");

        withNote.Note.ShouldBe("Same index, <1% drift.");
        withoutNote.Note.ShouldBeNull();
    }

    [Fact]
    public void ReplacePatterns_trims_dedupes_case_insensitively_and_drops_empties()
    {
        var substitution = CreateSubstitution();

        substitution.ReplacePatterns([
            "State Street S&P 500",
            "  state street s&p 500  ",
            "SSgA S&P 500",
            "",
            "   "
        ]);

        substitution.Patterns.ShouldBe(["State Street S&P 500", "SSgA S&P 500"]);
    }

    [Fact]
    public void ReplacePatterns_rejects_empty_collection()
    {
        var substitution = CreateSubstitution();

        Should.Throw<ArgumentException>(() => substitution.ReplacePatterns([]));
        Should.Throw<ArgumentException>(() => substitution.ReplacePatterns(["", "  "]));
    }

    [Fact]
    public void MatchesName_returns_true_for_case_insensitive_substring_hit()
    {
        var substitution = CreateSubstitution();
        substitution.ReplacePatterns(["State Street S&P 500", "SSgA S&P 500"]);

        substitution.MatchesName("My 401k State Street S&P 500 Index Non-Lending Class A").ShouldBeTrue();
        substitution.MatchesName("ssga s&p 500 fund").ShouldBeTrue();
    }

    [Fact]
    public void MatchesName_returns_false_when_no_pattern_matches()
    {
        var substitution = CreateSubstitution();
        substitution.ReplacePatterns(["State Street S&P 500"]);

        substitution.MatchesName("Vanguard Total Stock Market").ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MatchesName_returns_false_for_blank_input(string? input)
    {
        var substitution = CreateSubstitution();
        substitution.ReplacePatterns(["State Street S&P 500"]);

        substitution.MatchesName(input!).ShouldBeFalse();
    }

    [Fact]
    public void UpdateSubstitute_replaces_ticker_and_name()
    {
        var substitution = CreateSubstitution();

        substitution.UpdateSubstitute("agg", "iShares Core US Aggregate Bond ETF");

        substitution.SubstituteTicker.ShouldBe("AGG");
        substitution.SubstituteName.ShouldBe("iShares Core US Aggregate Bond ETF");
    }

    [Fact]
    public void SetFidelity_updates_fidelity()
    {
        var substitution = CreateSubstitution();

        substitution.SetFidelity(CitSubstitutionFidelity.Low);

        substitution.Fidelity.ShouldBe(CitSubstitutionFidelity.Low);
    }

    [Fact]
    public void CitName_defaults_to_null_when_not_supplied()
    {
        var substitution = CreateSubstitution();

        substitution.CitName.ShouldBeNull();
    }

    [Fact]
    public void Constructor_accepts_and_trims_cit_name()
    {
        var substitution = new CitSubstitution(
            "VOO",
            "Vanguard S&P 500 ETF",
            CitSubstitutionFidelity.High,
            null,
            "  State Street S&P 500 CIT  ");

        substitution.CitName.ShouldBe("State Street S&P 500 CIT");
    }

    [Fact]
    public void SetCitName_normalizes_blank_to_null()
    {
        var substitution = new CitSubstitution(
            "VOO", "Vanguard S&P 500 ETF", CitSubstitutionFidelity.High, null, "State Street S&P 500 CIT");

        substitution.SetCitName("   ");

        substitution.CitName.ShouldBeNull();
    }

    [Fact]
    public void SetCitName_updates_value()
    {
        var substitution = CreateSubstitution();

        substitution.SetCitName("State Street S&P 500 CIT");

        substitution.CitName.ShouldBe("State Street S&P 500 CIT");
    }
}
