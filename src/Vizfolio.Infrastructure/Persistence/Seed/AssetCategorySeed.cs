using Vizfolio.Domain.Reference;

namespace Vizfolio.Infrastructure.Persistence.Seed;

internal static class AssetCategorySeed
{
    // SEC N-PORT Item C.4.a "Asset category" enumeration.
    public static IReadOnlyList<AssetCategory> All { get; } =
    [
        new("STIV", "Short-Term Investment Vehicle", "Money market fund, liquidity pool, or other cash management vehicle."),
        new("EC", "Equity-Common", "Common equity securities."),
        new("EP", "Equity-Preferred", "Preferred equity securities."),
        new("DBT", "Debt", "Debt securities."),
        new("RA", "Repurchase Agreement", "Repurchase or reverse repurchase agreement."),
        new("LON", "Loan", "Loans and loan participations."),
        new("ABS-APCP", "ABS-Asset Backed Commercial Paper", "Asset-backed commercial paper."),
        new("ABS-CB", "ABS-Collateralized Bond/Debt Obligation", "Collateralized bond or debt obligation."),
        new("ABS-MBS", "ABS-Mortgage Backed Security", "Mortgage-backed security."),
        new("ABS-O", "ABS-Other", "Other asset-backed security not otherwise classified."),
        new("COMM", "Commodity", "Physical commodity or commodity-linked instrument."),
        new("DCO", "Derivative-Commodity", "Derivative referencing a commodity."),
        new("DCR", "Derivative-Credit", "Derivative referencing credit (e.g. credit default swap)."),
        new("DE", "Derivative-Equity", "Derivative referencing equity."),
        new("DFE", "Derivative-Foreign Exchange", "Derivative referencing foreign exchange."),
        new("DIR", "Derivative-Interest Rate", "Derivative referencing interest rates."),
        new("DO", "Derivative-Other", "Derivative not otherwise classified."),
        new("RE", "Real Estate", "Direct real estate or real estate-linked investment."),
    ];
}
