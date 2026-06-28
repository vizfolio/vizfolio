using Vizfolio.Domain.Common;

namespace Vizfolio.Domain.Funds;

public sealed class Fund : Entity
{
    private Fund() { }

    public Fund(string seriesId)
    {
        SeriesId = NormalizeSeriesId(seriesId);
    }

    public string SeriesId { get; private set; } = string.Empty;

    public string? Name { get; private set; }

    public string? RegistrantCik { get; private set; }

    public string? RegistrantName { get; private set; }

    public void UpdateProfile(string? name, string? registrantCik, string? registrantName)
    {
        Name = name;
        RegistrantCik = string.IsNullOrWhiteSpace(registrantCik) ? null : registrantCik.Trim();
        RegistrantName = registrantName;
    }

    private static string NormalizeSeriesId(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
            throw new ArgumentException("Series ID is required.", nameof(seriesId));

        return seriesId.Trim();
    }
}
