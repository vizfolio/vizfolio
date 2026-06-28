using System.Globalization;
using System.Text;
using System.Xml;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Parsers;

public sealed class QfxFileParser : IPortfolioFileParser
{
    public string SourceSystem => "QFX";

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

        var institution = SelectText(doc, "//FI/ORG");
        var accountNumber = SelectText(doc, "//INVACCTFROM/ACCTID")
                          ?? SelectText(doc, "//BANKACCTFROM/ACCTID")
                          ?? SelectText(doc, "//CCACCTFROM/ACCTID");

        var transactions = new List<ParsedTransaction>();
        ParseInvestmentTransactions(doc, transactions);
        ParseBankTransactions(doc, transactions);

        return new ParsedPortfolioFile(SourceSystem, institution, accountNumber, transactions);
    }

    private static void ParseInvestmentTransactions(XmlDocument doc, List<ParsedTransaction> transactions)
    {
        var invList = doc.SelectSingleNode("//INVTRANLIST");
        if (invList is null) return;

        foreach (XmlNode node in invList.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;
            var tx = node.Name.ToUpperInvariant() switch
            {
                "BUYSTOCK" or "BUYMF" or "BUYOTHER" => InvBuySell(node, TransactionType.Buy),
                "SELLSTOCK" or "SELLMF" or "SELLOTHER" => InvBuySell(node, TransactionType.Sell),
                "INCOME" => InvIncome(node),
                "REINVEST" => InvReinvest(node),
                "TRANSFER" => InvTransfer(node),
                _ => null,
            };
            if (tx is not null) transactions.Add(tx);
        }
    }

    private static void ParseBankTransactions(XmlDocument doc, List<ParsedTransaction> transactions)
    {
        var bankList = doc.SelectSingleNode("//BANKTRANLIST");
        if (bankList is null) return;

        foreach (XmlNode node in bankList.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;
            if (!node.Name.Equals("STMTTRN", StringComparison.OrdinalIgnoreCase)) continue;

            var fitId = SelectText(node, "FITID") ?? string.Empty;
            var trnType = SelectText(node, "TRNTYPE")?.ToUpperInvariant();
            var dtPosted = ParseDateOnly(SelectText(node, "DTPOSTED"));
            var amount = ParseDecimal(SelectText(node, "TRNAMT")) ?? 0m;
            var memo = SelectText(node, "MEMO");

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

            transactions.Add(new ParsedTransaction(
                ExternalId: fitId,
                Type: type,
                TradeDate: dtPosted,
                SettlementDate: null,
                Ticker: null,
                Cusip: null,
                Quantity: null,
                Price: null,
                Amount: amount,
                Fees: null,
                CurrencyCode: null,
                Memo: memo));
        }
    }

    private static ParsedTransaction InvBuySell(XmlNode node, TransactionType type)
    {
        var invtran = node.SelectSingleNode(".//INVTRAN");
        var secid = node.SelectSingleNode(".//SECID");
        var fitId = SelectText(invtran, "FITID") ?? string.Empty;
        var dtTrade = ParseDateOnly(SelectText(invtran, "DTTRADE"));
        var dtSettle = TryParseDateOnly(SelectText(invtran, "DTSETTLE"));
        var memo = SelectText(invtran, "MEMO");

        var ticker = SelectText(node, ".//SECID/UNIQUEID");
        var uniqueIdType = SelectText(node, ".//SECID/UNIQUEIDTYPE");
        var (mappedTicker, mappedCusip) = SplitSecurityId(ticker, uniqueIdType);

        var units = ParseDecimal(SelectText(node, ".//UNITS"));
        var unitPrice = ParseDecimal(SelectText(node, ".//UNITPRICE"));
        var commission = ParseDecimal(SelectText(node, ".//COMMISSION"));
        var fees = ParseDecimal(SelectText(node, ".//FEES"));
        var total = ParseDecimal(SelectText(node, ".//TOTAL")) ?? 0m;
        var currency = SelectText(node, ".//CURRENCY/CURSYM");

        var combinedFees = (commission ?? 0m) + (fees ?? 0m);
        return new ParsedTransaction(
            ExternalId: fitId,
            Type: type,
            TradeDate: dtTrade,
            SettlementDate: dtSettle,
            Ticker: mappedTicker,
            Cusip: mappedCusip,
            Quantity: units,
            Price: unitPrice,
            Amount: total,
            Fees: combinedFees == 0m ? null : combinedFees,
            CurrencyCode: currency,
            Memo: memo);
    }

    private static ParsedTransaction InvIncome(XmlNode node)
    {
        var invtran = node.SelectSingleNode(".//INVTRAN");
        var fitId = SelectText(invtran, "FITID") ?? string.Empty;
        var dtTrade = ParseDateOnly(SelectText(invtran, "DTTRADE"));
        var memo = SelectText(invtran, "MEMO");
        var incomeType = SelectText(node, "INCOMETYPE")?.ToUpperInvariant();

        var type = incomeType switch
        {
            "DIV" => TransactionType.Dividend,
            "INTEREST" => TransactionType.Interest,
            "CGLONG" or "CGSHORT" => TransactionType.CapitalGain,
            _ => TransactionType.Other,
        };

        var ticker = SelectText(node, ".//SECID/UNIQUEID");
        var uniqueIdType = SelectText(node, ".//SECID/UNIQUEIDTYPE");
        var (mappedTicker, mappedCusip) = SplitSecurityId(ticker, uniqueIdType);

        var total = ParseDecimal(SelectText(node, "TOTAL")) ?? 0m;
        var currency = SelectText(node, ".//CURRENCY/CURSYM");

        return new ParsedTransaction(
            ExternalId: fitId,
            Type: type,
            TradeDate: dtTrade,
            SettlementDate: null,
            Ticker: mappedTicker,
            Cusip: mappedCusip,
            Quantity: null,
            Price: null,
            Amount: total,
            Fees: null,
            CurrencyCode: currency,
            Memo: memo);
    }

    private static ParsedTransaction InvReinvest(XmlNode node)
    {
        var invtran = node.SelectSingleNode(".//INVTRAN");
        var fitId = SelectText(invtran, "FITID") ?? string.Empty;
        var dtTrade = ParseDateOnly(SelectText(invtran, "DTTRADE"));
        var memo = SelectText(invtran, "MEMO");

        var ticker = SelectText(node, ".//SECID/UNIQUEID");
        var uniqueIdType = SelectText(node, ".//SECID/UNIQUEIDTYPE");
        var (mappedTicker, mappedCusip) = SplitSecurityId(ticker, uniqueIdType);

        var units = ParseDecimal(SelectText(node, "UNITS"));
        var unitPrice = ParseDecimal(SelectText(node, "UNITPRICE"));
        var total = ParseDecimal(SelectText(node, "TOTAL")) ?? 0m;
        var currency = SelectText(node, ".//CURRENCY/CURSYM");

        return new ParsedTransaction(
            ExternalId: fitId,
            Type: TransactionType.Reinvest,
            TradeDate: dtTrade,
            SettlementDate: null,
            Ticker: mappedTicker,
            Cusip: mappedCusip,
            Quantity: units,
            Price: unitPrice,
            Amount: total,
            Fees: null,
            CurrencyCode: currency,
            Memo: memo);
    }

    private static ParsedTransaction InvTransfer(XmlNode node)
    {
        var invtran = node.SelectSingleNode(".//INVTRAN");
        var fitId = SelectText(invtran, "FITID") ?? string.Empty;
        var dtTrade = ParseDateOnly(SelectText(invtran, "DTTRADE"));
        var memo = SelectText(invtran, "MEMO");

        var ticker = SelectText(node, ".//SECID/UNIQUEID");
        var uniqueIdType = SelectText(node, ".//SECID/UNIQUEIDTYPE");
        var (mappedTicker, mappedCusip) = SplitSecurityId(ticker, uniqueIdType);

        var units = ParseDecimal(SelectText(node, "UNITS"));

        return new ParsedTransaction(
            ExternalId: fitId,
            Type: TransactionType.Transfer,
            TradeDate: dtTrade,
            SettlementDate: null,
            Ticker: mappedTicker,
            Cusip: mappedCusip,
            Quantity: units,
            Price: null,
            Amount: 0m,
            Fees: null,
            CurrencyCode: null,
            Memo: memo);
    }

    private static (string? Ticker, string? Cusip) SplitSecurityId(string? id, string? idType)
    {
        if (string.IsNullOrWhiteSpace(id)) return (null, null);
        var trimmed = id.Trim();
        var t = (idType ?? string.Empty).Trim().ToUpperInvariant();
        return t switch
        {
            "CUSIP" => (null, trimmed),
            "TICKER" => (trimmed, null),
            _ => (trimmed.Length is >= 1 and <= 8 ? trimmed : null,
                  trimmed.Length == 9 ? trimmed : null),
        };
    }

    private static string StripHeader(string raw)
    {
        var ofxStart = raw.IndexOf("<OFX>", StringComparison.OrdinalIgnoreCase);
        if (ofxStart < 0) throw new InvalidDataException("QFX file does not contain an <OFX> root element.");
        var body = raw[ofxStart..];

        // OFX 2.x preambles include an <?xml ...?> declaration before <OFX> and the body is well-formed XML.
        // OFX 1.x uses plain `KEY:VALUE` headers and SGML-style leaf tags without close tags.
        var preamble = raw[..ofxStart];
        var isSgml = preamble.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase) < 0;
        return isSgml ? SgmlToXml(body) : body;
    }

    private static string SgmlToXml(string body)
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

            var nameEnd = tag.IndexOfAny(new[] { ' ', '>' }, 1);
            var name = tag.Substring(1, nameEnd - 1);

            // Capture inline value text between this open tag and the next '<'.
            var valueStart = i;
            while (i < n && body[i] != '<' && body[i] != '\r' && body[i] != '\n') i++;
            var value = body.Substring(valueStart, i - valueStart).Trim();

            if (value.Length > 0)
            {
                // Escape XML-sensitive chars.
                value = value.Replace("&", "&amp;", StringComparison.Ordinal)
                             .Replace("<", "&lt;", StringComparison.Ordinal)
                             .Replace(">", "&gt;", StringComparison.Ordinal);
                sb.Append(value);
                sb.Append("</").Append(name).Append('>');
            }
        }

        return sb.ToString();
    }

    private static string? SelectText(XmlNode? root, string xpath)
    {
        if (root is null) return null;
        var n = root.SelectSingleNode(xpath);
        var text = n?.InnerText?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static DateOnly ParseDateOnly(string? raw)
    {
        var parsed = TryParseDateOnly(raw);
        if (parsed is null)
            throw new InvalidDataException($"Invalid QFX date value '{raw}'.");
        return parsed.Value;
    }

    private static DateOnly? TryParseDateOnly(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Length >= 8 ? raw[..8] : raw;
        if (DateOnly.TryParseExact(trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return null;
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
