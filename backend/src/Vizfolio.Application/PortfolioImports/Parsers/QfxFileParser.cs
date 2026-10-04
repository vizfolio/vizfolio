using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Brokers;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Parsers;

/// <summary>
/// Reads OFX/QFX statements — OFX 1.x SGML (with or without closed leaf tags) and OFX 2.x XML — covering the whole
/// OFX investment transaction vocabulary. Every aggregate either becomes a ledger row or a warning; one unreadable
/// row fails alone, never the file. Broker-specific conventions (sweep labels, what <c>AVAILCASH</c> means) come
/// from <see cref="BrokerProfiles"/>. See docs/performance-api.md → "QFX mapping".
/// </summary>
public sealed partial class QfxFileParser : IPortfolioFileParser
{
    private readonly BrokerProfiles _profiles;

    public QfxFileParser() : this(BrokerProfiles.BuiltIn)
    {
    }

    public QfxFileParser(BrokerProfiles profiles)
    {
        _profiles = profiles;
    }

    public string SourceSystem => "QFX";

    public string DisplayName => "OFX / QFX statement";

    public int Priority => 100;

    public IReadOnlyCollection<string> FileExtensions { get; } = [".qfx", ".ofx"];

    public async Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var head = new char[256];
        var read = await reader.ReadBlockAsync(head, cancellationToken);
        var probe = new string(head, 0, read);
        return probe.Contains("OFXHEADER", StringComparison.OrdinalIgnoreCase)
            || probe.Contains("<OFX>", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ParsedPortfolioFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(cancellationToken);

        var body = StripHeader(raw);
        var doc = new XmlDocument();
        doc.LoadXml(body);

        var warnings = new ImportWarningCollector();
        var securities = SecurityList.Build(doc);
        var statements = new List<ParsedAccountStatement>();

        foreach (XmlNode stmt in Nodes(doc, "//INVSTMTRS"))
            statements.Add(ParseInvestmentStatement(stmt, securities, warnings));

        AppendBankStatements(doc, "//STMTRS", ".//BANKACCTFROM", "BANKID", statements, warnings);
        AppendBankStatements(doc, "//CCSTMTRS", ".//CCACCTFROM", institutionCodeChild: null, statements, warnings);

        return new ParsedPortfolioFile(SourceSystem, statements) { Warnings = warnings.ToList() };
    }

    // ---------------- investment statements ----------------

    private ParsedAccountStatement ParseInvestmentStatement(XmlNode stmt, SecurityList securities, ImportWarningCollector warnings)
    {
        var acctFrom = stmt.SelectSingleNode(".//INVACCTFROM");
        var institutionCode = NormalizeCode(SelectText(acctFrom, "BROKERID"));
        var accountNumber = SelectText(acctFrom, "ACCTID");
        var asOf = TryParseDateOnly(SelectText(stmt, "DTASOF"));
        var profile = _profiles.For(institutionCode);
        var reader = new RowReader(securities, profile, warnings);

        var transactions = new List<ParsedTransaction>();
        if (stmt.SelectSingleNode(".//INVTRANLIST") is { } invList)
            foreach (var node in Elements(invList))
                reader.Read(node, transactions);

        var positions = new List<ParsedPosition>();
        if (stmt.SelectSingleNode(".//INVPOSLIST") is { } posList && asOf is not null)
            foreach (var node in Elements(posList))
                if (reader.ReadPosition(node, asOf.Value) is { } position) positions.Add(position);

        var cash = asOf is { } d ? ReadCashBalance(stmt.SelectSingleNode(".//INVBAL"), d, warnings) : null;
        (positions, cash) = MarkSettlementFund(transactions, positions, cash, profile);

        return new ParsedAccountStatement(institutionCode, accountNumber, transactions, positions, asOf, cash);
    }

    private static ParsedCashBalance? ReadCashBalance(XmlNode? invBal, DateOnly asOf, ImportWarningCollector warnings)
    {
        if (invBal is null) return null;
        var available = ParseDecimal(SelectText(invBal, "AVAILCASH"));
        if (available is null) return null;

        var margin = ParseDecimal(SelectText(invBal, "MARGINBALANCE"));
        var shortBalance = ParseDecimal(SelectText(invBal, "SHORTBALANCE"));
        if (margin is { } m && m != 0m || shortBalance is { } s && s != 0m)
            warnings.Add(ImportWarningCodes.MarginBalance,
                "The statement reports a margin or short balance. Only available cash is used to value the account.",
                asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return new ParsedCashBalance(asOf, available.Value, margin, shortBalance, IncludesSettlementFund: false);
    }

    /// <summary>
    /// Marks the settlement fund's position: a ticker the broker's sweep rows move, or the position whose market
    /// value is the statement's available cash when the profile says (or the statement shows) that's what it is.
    /// The cash balance then records that the position already carries it, so cash isn't counted twice.
    /// </summary>
    private static (List<ParsedPosition>, ParsedCashBalance?) MarkSettlementFund(
        List<ParsedTransaction> transactions, List<ParsedPosition> positions, ParsedCashBalance? cash, IBrokerProfile profile)
    {
        var sweepTickers = transactions
            .Where(t => t.IsSettlementFund && t.Ticker is not null)
            .Select(t => t.Ticker!.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var cashMatches = cash is { AvailableCash: not 0m } c
            ? positions.Where(p => p.MarketValue is { } mv && Math.Abs(mv - c.AvailableCash) < 0.01m).ToList()
            : [];
        var cashPosition = cashMatches.Count == 1 ? cashMatches[0] : null;
        var includes = profile.AvailableCashIncludesSettlementFund ?? cashPosition is not null;

        var marked = positions
            .Select(p =>
            {
                var isSettlement = (p.Ticker is not null && sweepTickers.Contains(p.Ticker.ToUpperInvariant()))
                                   || (includes && ReferenceEquals(p, cashPosition));
                return isSettlement ? p with { IsSettlementFund = true } : p;
            })
            .ToList();

        return (marked, cash is null ? null : cash with { IncludesSettlementFund = includes });
    }

    /// <summary>Turns each OFX investment aggregate into ledger rows (or a warning), one row's failure at a time.</summary>
    private sealed class RowReader(SecurityList securities, IBrokerProfile profile, ImportWarningCollector warnings)
    {
        public void Read(XmlNode node, List<ParsedTransaction> sink)
        {
            var tag = node.Name.ToUpperInvariant();
            if (tag is "DTSTART" or "DTEND") return;

            try
            {
                switch (tag)
                {
                    case "BUYSTOCK" or "BUYMF" or "BUYOTHER" or "BUYDEBT":
                        sink.Add(Trade(node, TransactionType.Buy, tag));
                        break;
                    case "SELLSTOCK" or "SELLMF" or "SELLOTHER" or "SELLDEBT":
                        sink.Add(Trade(node, TransactionType.Sell, tag));
                        break;
                    case "BUYOPT" or "SELLOPT":
                        sink.Add(Trade(node, tag == "BUYOPT" ? TransactionType.Buy : TransactionType.Sell, tag));
                        warnings.Add(ImportWarningCodes.OptionActivity,
                            "Option trades were imported, but options can't be priced: the account shows as incomplete while they're held.",
                            Sample(node));
                        break;
                    case "CLOSUREOPT":
                        sink.Add(OptionClosure(node));
                        break;
                    case "INCOME":
                        Income(node, sink);
                        break;
                    case "REINVEST":
                        sink.Add(Reinvest(node));
                        break;
                    case "TRANSFER":
                        sink.Add(Transfer(node));
                        break;
                    case "RETOFCAP":
                        sink.Add(Cash(node, TransactionType.ReturnOfCapital, tag, amount => amount));
                        break;
                    case "MARGININTEREST":
                        sink.Add(Cash(node, TransactionType.Interest, tag, amount => -Math.Abs(amount)));
                        break;
                    case "INVEXPENSE":
                        sink.Add(Cash(node, TransactionType.Fee, tag, amount => -Math.Abs(amount)));
                        break;
                    case "SPLIT":
                        sink.Add(Split(node));
                        break;
                    case "JRNLSEC" or "JRNLFUND":
                        sink.Add(Journal(node, tag));
                        break;
                    case "INVBANKTRAN":
                        if (ReadStatementTransaction(node.SelectSingleNode(".//STMTTRN")) is { } bankRow)
                            sink.Add(bankRow with { SubAccount = SelectText(node, "SUBACCTFUND") });
                        break;
                    default:
                        warnings.Add(ImportWarningCodes.UnknownAggregate,
                            $"<{tag}> isn't a transaction type Vizfolio understands; its rows were not imported.",
                            Sample(node));
                        break;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException)
            {
                warnings.Add(ImportWarningCodes.RowFailed, $"A <{tag}> row couldn't be read and was skipped: {ex.Message}", Sample(node));
            }
        }

        public ParsedPosition? ReadPosition(XmlNode node, DateOnly asOf)
        {
            var tag = node.Name.ToUpperInvariant();
            if (tag is not ("POSSTOCK" or "POSMF" or "POSOPT" or "POSOTHER" or "POSDEBT"))
            {
                warnings.Add(ImportWarningCodes.UnknownAggregate,
                    $"<{tag}> isn't a position type Vizfolio understands; it was not recorded.", tag);
                return null;
            }

            var (ticker, cusip) = Security(node);
            var units = ParseDecimal(SelectText(node, ".//UNITS")) ?? 0m;
            var marketValue = ParseDecimal(SelectText(node, ".//MKTVAL"));

            // A short position is held negative (OFX reports its units unsigned).
            if (string.Equals(SelectText(node, ".//POSTYPE"), "SHORT", StringComparison.OrdinalIgnoreCase))
            {
                units = -Math.Abs(units);
                if (marketValue is { } mv) marketValue = -Math.Abs(mv);
            }

            if (tag == "POSOPT" && units != 0m)
                warnings.Add(ImportWarningCodes.OptionActivity,
                    "The statement holds options, which can't be priced: the account shows as incomplete while they're held.",
                    ticker ?? cusip);

            return new ParsedPosition(
                AsOf: asOf,
                Ticker: ticker,
                Cusip: cusip,
                Units: units,
                UnitPrice: ParseDecimal(SelectText(node, ".//UNITPRICE")),
                MarketValue: marketValue,
                CostBasis: ParseDecimal(SelectText(node, ".//COSTBASIS")),
                CurrencyCode: SelectText(node, ".//CURRENCY/CURSYM"),
                PriceAsOf: TryParseDateOnly(SelectText(node, ".//DTPRICEASOF")));
        }

        private ParsedTransaction Trade(XmlNode node, TransactionType type, string tag)
        {
            var header = InvTran.Read(node);
            var (ticker, cusip) = Security(node);
            var fees = (ParseDecimal(SelectText(node, ".//COMMISSION")) ?? 0m)
                       + (ParseDecimal(SelectText(node, ".//FEES")) ?? 0m)
                       + (ParseDecimal(SelectText(node, ".//LOAD")) ?? 0m);

            return new ParsedTransaction(
                ExternalId: header.FitId,
                Type: type,
                TradeDate: header.TradeDate,
                SettlementDate: header.SettleDate,
                Ticker: ticker,
                Cusip: cusip,
                Quantity: ParseDecimal(SelectText(node, ".//UNITS")),
                Price: ParseDecimal(SelectText(node, ".//UNITPRICE")),
                Amount: ParseDecimal(SelectText(node, ".//TOTAL")) ?? 0m,
                Fees: fees == 0m ? null : fees,
                CurrencyCode: SelectText(node, ".//CURRENCY/CURSYM"),
                Memo: header.Memo,
                SourceType: TradeLabel(node, tag),
                IsSettlementFund: profile.IsSweepMemo(header.Memo),
                SubAccount: SubAccount(node));
        }

        /// <summary>
        /// INCOME: the row is stored gross (<c>TOTAL</c>); tax withheld (<c>WITHHOLDING</c> + <c>TAXES</c>) becomes its
        /// own Fee row (<c>{FITID}:wh</c>) so the cash effect nets correctly (roadmap Appendix A.3).
        /// </summary>
        private void Income(XmlNode node, List<ParsedTransaction> sink)
        {
            var header = InvTran.Read(node);
            var (ticker, cusip) = Security(node);
            var incomeType = SelectText(node, "INCOMETYPE")?.ToUpperInvariant();
            var type = incomeType switch
            {
                "DIV" => TransactionType.Dividend,
                "INTEREST" => TransactionType.Interest,
                "CGLONG" or "CGSHORT" => TransactionType.CapitalGain,
                _ => TransactionType.Other,
            };
            if (type == TransactionType.Other)
                warnings.Add(ImportWarningCodes.UnmappedLabel,
                    $"Income of type \"{incomeType ?? "(none)"}\" was imported as Other: cash it paid is counted, but it isn't reported as dividend or interest.",
                    Sample(node));

            var currency = SelectText(node, ".//CURRENCY/CURSYM");
            var subAccount = SubAccount(node);
            sink.Add(new ParsedTransaction(
                ExternalId: header.FitId,
                Type: type,
                TradeDate: header.TradeDate,
                SettlementDate: header.SettleDate,
                Ticker: ticker,
                Cusip: cusip,
                Quantity: null,
                Price: null,
                Amount: ParseDecimal(SelectText(node, "TOTAL")) ?? 0m,
                Fees: null,
                CurrencyCode: currency,
                Memo: header.Memo,
                SourceType: type == TransactionType.Other ? $"INCOME {incomeType}".TrimEnd() : null,
                SubAccount: subAccount));

            var withheld = (ParseDecimal(SelectText(node, "WITHHOLDING")) ?? 0m) + (ParseDecimal(SelectText(node, "TAXES")) ?? 0m);
            if (withheld > 0m)
                sink.Add(new ParsedTransaction(
                    ExternalId: string.IsNullOrEmpty(header.FitId) ? null : $"{header.FitId}:wh",
                    Type: TransactionType.Fee,
                    TradeDate: header.TradeDate,
                    SettlementDate: header.SettleDate,
                    Ticker: ticker,
                    Cusip: cusip,
                    Quantity: null,
                    Price: null,
                    Amount: -withheld,
                    Fees: null,
                    CurrencyCode: currency,
                    Memo: header.Memo,
                    SourceType: "Tax withheld",
                    SubAccount: subAccount));
        }

        private ParsedTransaction Reinvest(XmlNode node)
        {
            var header = InvTran.Read(node);
            var (ticker, cusip) = Security(node);
            var incomeType = SelectText(node, "INCOMETYPE")?.ToUpperInvariant();

            return new ParsedTransaction(
                ExternalId: header.FitId,
                Type: TransactionType.Reinvest,
                TradeDate: header.TradeDate,
                SettlementDate: header.SettleDate,
                Ticker: ticker,
                Cusip: cusip,
                Quantity: ParseDecimal(SelectText(node, "UNITS")),
                Price: ParseDecimal(SelectText(node, "UNITPRICE")),
                Amount: ParseDecimal(SelectText(node, "TOTAL")) ?? 0m,
                Fees: null,
                CurrencyCode: SelectText(node, ".//CURRENCY/CURSYM"),
                Memo: header.Memo,
                SourceType: null,
                IsSettlementFund: profile.IsSweepMemo(header.Memo),
                SubAccount: SubAccount(node));
        }

        /// <summary>
        /// An in-kind transfer: shares cross the account boundary with no cash (valuation prices them on the day).
        /// <c>TFERACTION</c> gives the direction — brokers don't reliably sign <c>UNITS</c>, so a transfer out must
        /// never add shares.
        /// </summary>
        private ParsedTransaction Transfer(XmlNode node)
        {
            var header = InvTran.Read(node);
            var (ticker, cusip) = Security(node);
            var action = SelectText(node, "TFERACTION")?.ToUpperInvariant();
            var units = ParseDecimal(SelectText(node, "UNITS"));
            if (units is { } u)
                units = action switch
                {
                    "OUT" => -Math.Abs(u),
                    "IN" => Math.Abs(u),
                    _ => u,
                };

            return new ParsedTransaction(
                ExternalId: header.FitId,
                Type: TransactionType.Transfer,
                TradeDate: header.TradeDate,
                SettlementDate: header.SettleDate,
                Ticker: ticker,
                Cusip: cusip,
                Quantity: units,
                Price: ParseDecimal(SelectText(node, "UNITPRICE")),
                Amount: 0m,
                Fees: null,
                CurrencyCode: null,
                Memo: header.Memo,
                SourceType: null,
                SubAccount: SelectText(node, "SUBACCTSEC"));
        }

        /// <summary>A cash-only aggregate tied to a security (or none): RETOFCAP, MARGININTEREST, INVEXPENSE.</summary>
        private ParsedTransaction Cash(XmlNode node, TransactionType type, string tag, Func<decimal, decimal> sign)
        {
            var header = InvTran.Read(node);
            var (ticker, cusip) = Security(node);
            return new ParsedTransaction(
                ExternalId: header.FitId,
                Type: type,
                TradeDate: header.TradeDate,
                SettlementDate: header.SettleDate,
                Ticker: ticker,
                Cusip: cusip,
                Quantity: null,
                Price: null,
                Amount: sign(ParseDecimal(SelectText(node, "TOTAL")) ?? 0m),
                Fees: null,
                CurrencyCode: SelectText(node, ".//CURRENCY/CURSYM"),
                Memo: header.Memo,
                SourceType: tag,
                SubAccount: SubAccount(node));
        }

        /// <summary>
        /// SPLIT: the ratio (<c>NUMERATOR</c>:<c>DENOMINATOR</c>) and the change in shares (<c>NEWUNITS − OLDUNITS</c>).
        /// Cash paid for fractional shares (<c>FRACCASH</c>) is the row's amount.
        /// </summary>
        private ParsedTransaction Split(XmlNode node)
        {
            var header = InvTran.Read(node);
            var (ticker, cusip) = Security(node);
            var numerator = ParseDecimal(SelectText(node, "NUMERATOR"));
            var denominator = ParseDecimal(SelectText(node, "DENOMINATOR"));
            var oldUnits = ParseDecimal(SelectText(node, "OLDUNITS"));
            var newUnits = ParseDecimal(SelectText(node, "NEWUNITS"));
            if (numerator is not > 0m || denominator is not > 0m)
            {
                numerator = denominator = null;
                warnings.Add(ImportWarningCodes.SplitWithoutRatio,
                    "A split was reported without a ratio; only its change in shares is applied.", Sample(node));
            }

            return new ParsedTransaction(
                ExternalId: header.FitId,
                Type: TransactionType.Split,
                TradeDate: header.TradeDate,
                SettlementDate: header.SettleDate,
                Ticker: ticker,
                Cusip: cusip,
                Quantity: newUnits is { } n && oldUnits is { } o ? n - o : null,
                Price: null,
                Amount: ParseDecimal(SelectText(node, "FRACCASH")) ?? 0m,
                Fees: null,
                CurrencyCode: SelectText(node, ".//CURRENCY/CURSYM"),
                Memo: header.Memo,
                SourceType: "SPLIT",
                SplitNumerator: numerator,
                SplitDenominator: denominator,
                SubAccount: SelectText(node, "SUBACCTSEC"));
        }

        /// <summary>JRNLSEC / JRNLFUND: shares or cash moved between the account's own sub-accounts — neutral overall.</summary>
        private ParsedTransaction Journal(XmlNode node, string tag)
        {
            var header = InvTran.Read(node);
            var (ticker, cusip) = tag == "JRNLSEC" ? Security(node) : (null, null);
            var from = SelectText(node, "SUBACCTFROM");
            var to = SelectText(node, "SUBACCTTO");

            return new ParsedTransaction(
                ExternalId: header.FitId,
                Type: TransactionType.Journal,
                TradeDate: header.TradeDate,
                SettlementDate: header.SettleDate,
                Ticker: ticker,
                Cusip: cusip,
                Quantity: tag == "JRNLSEC" ? ParseDecimal(SelectText(node, "UNITS")) : null,
                Price: null,
                Amount: tag == "JRNLFUND" ? ParseDecimal(SelectText(node, "TOTAL")) ?? 0m : 0m,
                Fees: null,
                CurrencyCode: null,
                Memo: header.Memo,
                SourceType: tag,
                SubAccount: from is null && to is null ? null : $"{from}→{to}");
        }

        /// <summary>
        /// CLOSUREOPT (exercise, assignment, expiry): kept as Other with a warning. Its effect on the option position
        /// isn't signed consistently across brokers, so no shares are moved.
        /// </summary>
        private ParsedTransaction OptionClosure(XmlNode node)
        {
            var header = InvTran.Read(node);
            var (ticker, cusip) = Security(node);
            var action = SelectText(node, "OPTACTION")?.ToUpperInvariant();
            warnings.Add(ImportWarningCodes.OptionActivity,
                "An option was exercised, assigned or expired. It was recorded without changing positions; check the account's holdings.",
                Sample(node));

            return new ParsedTransaction(
                ExternalId: header.FitId,
                Type: TransactionType.Other,
                TradeDate: header.TradeDate,
                SettlementDate: header.SettleDate,
                Ticker: ticker,
                Cusip: cusip,
                Quantity: null,
                Price: null,
                Amount: 0m,
                Fees: null,
                CurrencyCode: null,
                Memo: header.Memo,
                SourceType: action is null ? "CLOSUREOPT" : $"CLOSUREOPT {action}",
                SubAccount: SelectText(node, "SUBACCTSEC"));
        }

        private (string? Ticker, string? Cusip) Security(XmlNode node)
        {
            var id = SelectText(node, ".//SECID/UNIQUEID");
            var idType = SelectText(node, ".//SECID/UNIQUEIDTYPE");
            var resolved = securities.Resolve(id, idType);
            if (resolved.Ticker is null && resolved.Cusip is null && id is not null)
                warnings.Add(ImportWarningCodes.UnsupportedSecurityId,
                    $"A security identified by {idType ?? "an unknown ID type"} has no ticker or CUSIP; its rows aren't linked to a holding.",
                    id);
            return resolved;
        }

        /// <summary>
        /// The trade's label when it says more than Buy/Sell — a short sale (<c>SELLSHORT</c>), a cover
        /// (<c>BUYTOCOVER</c>), an option or a bond — so the ledger shows it. Plain buys and sells stay unlabelled.
        /// </summary>
        private static string? TradeLabel(XmlNode node, string tag)
        {
            var label = SelectText(node, "BUYTYPE") ?? SelectText(node, "SELLTYPE")
                        ?? SelectText(node, "OPTBUYTYPE") ?? SelectText(node, "OPTSELLTYPE");
            if (tag.EndsWith("OPT", StringComparison.Ordinal) || tag.EndsWith("DEBT", StringComparison.Ordinal))
                return label is null ? tag : $"{tag} {label}";
            return label is null or "BUY" or "SELL" ? null : label;
        }

        private static string? SubAccount(XmlNode node) => SelectText(node, ".//SUBACCTSEC") ?? SelectText(node, ".//SUBACCTFUND");

        private static string Sample(XmlNode node)
            => SelectText(node, ".//FITID") is { } fitId ? $"{node.Name} {fitId}" : node.Name;
    }

    /// <summary>The shared <c>&lt;INVTRAN&gt;</c> header of every investment transaction.</summary>
    private readonly record struct InvTran(string FitId, DateOnly TradeDate, DateOnly? SettleDate, string? Memo)
    {
        public static InvTran Read(XmlNode node)
        {
            var invtran = node.SelectSingleNode(".//INVTRAN");
            return new InvTran(
                SelectText(invtran, "FITID") ?? string.Empty,
                ParseDateOnly(SelectText(invtran, "DTTRADE")),
                TryParseDateOnly(SelectText(invtran, "DTSETTLE")),
                SelectText(invtran, "MEMO"));
        }
    }

    /// <summary>
    /// The file's <c>&lt;SECLIST&gt;</c>: maps a security's id (CUSIP, ISIN, or a broker's own type) to its ticker.
    /// </summary>
    private sealed class SecurityList
    {
        private readonly Dictionary<string, string> _tickerById = new(StringComparer.OrdinalIgnoreCase);

        public static SecurityList Build(XmlDocument doc)
        {
            var list = new SecurityList();
            foreach (XmlNode secInfo in Nodes(doc, "//SECLIST//SECINFO"))
            {
                var uniqueId = SelectText(secInfo, "SECID/UNIQUEID");
                var ticker = SelectText(secInfo, "TICKER");
                if (!string.IsNullOrWhiteSpace(uniqueId) && !string.IsNullOrWhiteSpace(ticker))
                    list._tickerById[uniqueId.Trim()] = ticker.Trim();
            }

            return list;
        }

        /// <summary>
        /// A ticker id is the ticker. A CUSIP (or an untyped 9-character id) keeps its CUSIP and gets the SECLIST
        /// ticker. A US/Canadian ISIN carries its CUSIP inside it. Any other id is never mistaken for a ticker.
        /// </summary>
        public (string? Ticker, string? Cusip) Resolve(string? id, string? idType)
        {
            if (string.IsNullOrWhiteSpace(id)) return (null, null);
            var trimmed = id.Trim();
            var type = (idType ?? string.Empty).Trim().ToUpperInvariant();
            var ticker = _tickerById.GetValueOrDefault(trimmed);

            if (type == "TICKER") return (trimmed, null);
            if (type == "CUSIP" || (type.Length == 0 && trimmed.Length == 9)) return (ticker, trimmed);
            if (type == "ISIN" && trimmed.Length == 12 && (trimmed.StartsWith("US", StringComparison.OrdinalIgnoreCase)
                                                            || trimmed.StartsWith("CA", StringComparison.OrdinalIgnoreCase)))
                return (ticker, trimmed.Substring(2, 9));
            return (ticker, null);
        }
    }

    // ---------------- bank statements ----------------

    private static void AppendBankStatements(
        XmlDocument doc,
        string statementXPath,
        string acctFromXPath,
        string? institutionCodeChild,
        List<ParsedAccountStatement> sink,
        ImportWarningCollector warnings)
    {
        foreach (XmlNode stmt in Nodes(doc, statementXPath))
        {
            var acctFrom = stmt.SelectSingleNode(acctFromXPath);
            var institutionCode = institutionCodeChild is null ? null : NormalizeCode(SelectText(acctFrom, institutionCodeChild));
            var transactions = new List<ParsedTransaction>();
            if (stmt.SelectSingleNode(".//BANKTRANLIST") is { } bankList)
            {
                foreach (var node in Elements(bankList))
                {
                    if (!node.Name.Equals("STMTTRN", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        if (ReadStatementTransaction(node) is { } row) transactions.Add(row);
                    }
                    catch (InvalidDataException ex)
                    {
                        warnings.Add(ImportWarningCodes.RowFailed, $"A bank row couldn't be read and was skipped: {ex.Message}",
                            SelectText(node, "FITID"));
                    }
                }
            }

            var asOf = TryParseDateOnly(SelectText(stmt, ".//BANKTRANLIST/DTEND"));
            sink.Add(new ParsedAccountStatement(institutionCode, SelectText(acctFrom, "ACCTID"), transactions, [], asOf));
        }
    }

    private static ParsedTransaction? ReadStatementTransaction(XmlNode? stmtTrn)
    {
        if (stmtTrn is null) return null;

        var trnType = SelectText(stmtTrn, "TRNTYPE")?.ToUpperInvariant();
        var amount = ParseDecimal(SelectText(stmtTrn, "TRNAMT")) ?? 0m;
        var type = trnType switch
        {
            "CREDIT" or "DEP" or "DIRECTDEP" => TransactionType.Deposit,
            "DEBIT" or "DIRECTDEBIT" or "PAYMENT" => TransactionType.Withdrawal,
            "INT" => TransactionType.Interest,
            "DIV" => TransactionType.Dividend,
            "FEE" or "SRVCHG" => TransactionType.Fee,
            "XFER" => TransactionType.Transfer,
            _ => amount >= 0 ? TransactionType.Deposit : TransactionType.Withdrawal,
        };

        return new ParsedTransaction(
            ExternalId: SelectText(stmtTrn, "FITID") ?? string.Empty,
            Type: type,
            TradeDate: ParseDateOnly(SelectText(stmtTrn, "DTPOSTED")),
            SettlementDate: null,
            Ticker: null,
            Cusip: null,
            Quantity: null,
            Price: null,
            Amount: amount,
            Fees: null,
            CurrencyCode: null,
            Memo: SelectText(stmtTrn, "MEMO") ?? SelectText(stmtTrn, "NAME"));
    }

    // ---------------- SGML → XML ----------------

    private static string StripHeader(string raw)
    {
        var ofxStart = raw.IndexOf("<OFX>", StringComparison.OrdinalIgnoreCase);
        if (ofxStart < 0) throw new InvalidDataException("QFX file does not contain an <OFX> root element.");
        var body = raw[ofxStart..];

        // OFX 2.x preambles include an <?xml ...?> declaration before <OFX> and the body is well-formed XML.
        // OFX 1.x uses plain `KEY:VALUE` headers and SGML-style leaf tags, which may or may not be closed.
        var preamble = raw[..ofxStart];
        var isSgml = preamble.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase) < 0;
        return isSgml ? SgmlToXml(body) : body;
    }

    /// <summary>
    /// Closes OFX 1.x SGML leaf elements (<c>&lt;FITID&gt;123</c> → <c>&lt;FITID&gt;123&lt;/FITID&gt;</c>), leaving a
    /// leaf the file already closes alone, and escapes bare <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c> in values without
    /// double-escaping entities the file already uses (<c>&amp;amp;</c>).
    /// </summary>
    internal static string SgmlToXml(string body)
    {
        var sb = new StringBuilder(body.Length + 256);
        var i = 0;
        var n = body.Length;
        while (i < n)
        {
            if (body[i] != '<') { sb.Append(body[i]); i++; continue; }

            var tagEnd = body.IndexOf('>', i + 1);
            if (tagEnd < 0) { sb.Append(body[i..]); break; }

            var tag = body.Substring(i, tagEnd - i + 1);
            sb.Append(tag);
            i = tagEnd + 1;

            if (tag.StartsWith("</", StringComparison.Ordinal)) continue;
            if (tag.EndsWith("/>", StringComparison.Ordinal)) continue;

            var nameEnd = tag.IndexOfAny([' ', '>'], 1);
            var name = tag.Substring(1, nameEnd - 1);

            // Capture inline value text between this open tag and the next '<' (or line end).
            var valueStart = i;
            while (i < n && body[i] != '<' && body[i] != '\r' && body[i] != '\n') i++;
            var value = body.Substring(valueStart, i - valueStart).Trim();
            if (value.Length == 0) continue;

            sb.Append(EscapeValue(value));
            var alreadyClosed = string.Compare(body, i, $"</{name}>", 0, name.Length + 3, StringComparison.OrdinalIgnoreCase) == 0;
            if (!alreadyClosed) sb.Append("</").Append(name).Append('>');
        }

        return sb.ToString();
    }

    private static string EscapeValue(string value)
        => BareAmpersand().Replace(value, "&amp;")
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    // An '&' that doesn't already start an XML entity (&amp; &lt; &gt; &quot; &apos; &#123; &#x1F;).
    [GeneratedRegex(@"&(?!(?:amp|lt|gt|quot|apos|#\d+|#x[0-9A-Fa-f]+);)")]
    private static partial Regex BareAmpersand();

    // ---------------- helpers ----------------

    private static IEnumerable<XmlNode> Nodes(XmlNode root, string xpath)
        => root.SelectNodes(xpath)?.Cast<XmlNode>() ?? [];

    private static IEnumerable<XmlNode> Elements(XmlNode parent)
        => parent.ChildNodes.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element);

    private static string? SelectText(XmlNode? root, string xpath)
    {
        if (root is null) return null;
        var n = root.SelectSingleNode(xpath);
        var text = n?.InnerText?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? NormalizeCode(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();

    private static DateOnly ParseDateOnly(string? raw)
        => TryParseDateOnly(raw) ?? throw new InvalidDataException($"invalid date '{raw}'.");

    private static DateOnly? TryParseDateOnly(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Length >= 8 ? raw[..8] : raw;
        return DateOnly.TryParseExact(trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
