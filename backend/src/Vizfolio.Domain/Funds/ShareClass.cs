namespace Vizfolio.Domain.Funds;

public sealed class ShareClass
{
    private ShareClass() { }

    public ShareClass(string classId, string? name, string? ticker, decimal? expenseRatio)
    {
        if (string.IsNullOrWhiteSpace(classId))
            throw new ArgumentException("Class ID is required.", nameof(classId));

        if (expenseRatio is < 0)
            throw new ArgumentOutOfRangeException(nameof(expenseRatio), "Expense ratio cannot be negative.");

        ClassId = classId.Trim();
        Name = name;
        Ticker = string.IsNullOrWhiteSpace(ticker) ? null : ticker.Trim().ToUpperInvariant();
        ExpenseRatio = expenseRatio;
    }

    public string ClassId { get; private set; } = string.Empty;

    public string? Name { get; private set; }

    public string? Ticker { get; private set; }

    public decimal? ExpenseRatio { get; private set; }
}
