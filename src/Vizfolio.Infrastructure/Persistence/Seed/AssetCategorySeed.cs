using Vizfolio.Domain.Reference;

namespace Vizfolio.Infrastructure.Persistence.Seed;

internal static class AssetCategorySeed
{
    // Coarse asset-class buckets emitted by the vizfolio-extract pipeline
    // (see edgar-extract/pipeline/mappings.py — SEC N-PORT codes are collapsed
    // into these four categories before the snapshot JSON is written).
    public static IReadOnlyList<AssetCategory> All { get; } =
    [
        new("EQUITY", "Equity", "Equity securities (common or preferred)."),
        new("DEBT", "Debt", "Debt, structured notes, loans, and asset-backed securities."),
        new("DERIVATIVE", "Derivative", "Equity, credit, rate, commodity, FX, or other derivatives."),
        new("OTHER", "Other", "Short-term investments, repurchase agreements, commodities, real estate, and anything not otherwise classified."),
    ];
}
