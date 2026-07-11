namespace Vizfolio.Domain.Funds;

public sealed class MonthlyReturn
{
    private MonthlyReturn() { }

    public MonthlyReturn(DateOnly month, decimal returnPct, string? classId)
    {
        Month = new DateOnly(month.Year, month.Month, 1);
        ReturnPct = returnPct;
        ClassId = string.IsNullOrWhiteSpace(classId) ? null : classId.Trim();
    }

    public DateOnly Month { get; private set; }

    public decimal ReturnPct { get; private set; }

    public string? ClassId { get; private set; }
}
