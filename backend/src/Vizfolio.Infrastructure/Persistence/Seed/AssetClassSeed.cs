using Vizfolio.Domain.Reference;

namespace Vizfolio.Infrastructure.Persistence.Seed;

internal static class AssetClassSeed
{
    // Coarse asset-class buckets emitted by the vizfolio-extract pipeline
    // (see edgar-extract/pipeline/mappings.py — SEC N-PORT asset_category
    // codes are mapped down to one of these buckets).
    public static IReadOnlyList<AssetClass> All { get; } =
    [
        new("EQUITY", "Equity", "Equity securities (common or preferred)."),
        new("DEBT", "Debt", "Debt, structured notes, loans, and asset-backed securities."),
        new("DERIVATIVE", "Derivative", "Equity, credit, rate, commodity, FX, or other derivatives."),
        new("CASH", "Cash", "Short-term investment vehicles and repurchase agreements (cash-equivalent positions)."),
        new("COMMODITY", "Commodity", "Physical commodities or commodity-linked instruments."),
        new("REAL_ESTATE", "Real Estate", "Direct real estate or real estate-linked investments."),
        new("OTHER", "Other", "Anything not otherwise classified."),
    ];
}
