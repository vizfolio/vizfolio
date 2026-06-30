using Vizfolio.Domain.Reference;

namespace Vizfolio.Infrastructure.Persistence.Seed;

internal static class AssetCategorySeed
{
    // SEC N-PORT Item C.4.a "Asset category" enumeration, as emitted by the
    // vizfolio-extract pipeline (see edgar-extract/pipeline/mappings.py).
    // The coarse rollup (equity / debt / derivative / other) lives in AssetClass.
    public static IReadOnlyList<AssetCategory> All { get; } =
    [
        new("EC", "Equity-Common", "Common equity securities."),
        new("EP", "Equity-Preferred", "Preferred equity securities."),
        new("DBT", "Debt", "Debt securities."),
        new("SN", "Structured Note", "Structured note."),
        new("LON", "Loan", "Loans and loan participations."),
        new("ABS-APCP", "ABS-Asset Backed Commercial Paper", "Asset-backed commercial paper."),
        new("ABS-CBDO", "ABS-Collateralized Bond/Debt Obligation", "Collateralized bond or debt obligation."),
        new("ABS-MBS", "ABS-Mortgage Backed Security", "Mortgage-backed security."),
        new("ABS-O", "ABS-Other", "Other asset-backed security not otherwise classified."),
        new("DE", "Derivative-Equity", "Derivative referencing equity."),
        new("DCR", "Derivative-Credit", "Derivative referencing credit (e.g. credit default swap)."),
        new("DIR", "Derivative-Interest Rate", "Derivative referencing interest rates."),
        new("DCO", "Derivative-Commodity", "Derivative referencing a commodity."),
        new("DFE", "Derivative-Foreign Exchange", "Derivative referencing foreign exchange."),
        new("DOT", "Derivative-Other", "Derivative not otherwise classified."),
        new("STIV", "Short-Term Investment Vehicle", "Money market fund, liquidity pool, or other cash management vehicle."),
        new("RA", "Repurchase Agreement", "Repurchase or reverse repurchase agreement."),
        new("COMD", "Commodity", "Physical commodity or commodity-linked instrument."),
        new("RE", "Real Estate", "Direct real estate or real estate-linked investment."),
        new("OTH", "Other", "Other asset category not otherwise classified."),
        new("OTHER", "Other", "Alias used by some N-PORT filings for OTH."),
    ];
}
