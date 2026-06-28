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

        var securityList = BuildSecurityList(doc);
        var statements = new List<ParsedAccountStatement>();

        AppendStatements(doc, "//INVSTMTRS", ".//INVACCTFROM", "BROKERID",
            (stmt, txs) =>
            {
                var invList = stmt.SelectSingleNode(".//INVTRANLIST");
                if (invList is not null) ParseInvestmentTransactions(invList, txs, securityList);
            },
            statements);

        AppendStatements(doc, "//STMTRS", ".//BANKACCTFROM", "BANKID",
            (stmt, txs) =>
            {
                var bankList = stmt.SelectSingleNode(".//BANKTRANLIST");
                if (bankList is not null) ParseBankTransactions(bankList, txs);
            },
            statements);

        AppendStatements(doc, "//CCSTMTRS", ".//CCACCTFROM", institutionCodeChild: null,
            (stmt, txs) =>
            {
                var bankList = stmt.SelectSingleNode(".//BANKTRANLIST");
                if (bankList is not null) ParseBankTransactions(bankList, txs);
            },
            statements);

        return new ParsedPortfolioFile(SourceSystem, statements);
    }

    private static void AppendStatements(
        XmlDocument doc,
        string statementXPath,
        string acctFromXPath,
        string? institutionCodeChild,
        Action<XmlNode, List<ParsedTransaction>> populateTransactions,
        List<ParsedAccountStatement> sink)
    {
        var nodes = doc.SelectNodes(statementXPath);
        if (nodes is null) return;

        foreach (XmlNode stmt in nodes)
        {
            var acctFrom = stmt.SelectSingleNode(acctFromXPath);
            var institutionCode = institutionCodeChild is null
                ? null
                : NormalizeCode(SelectText(acctFrom, institutionCodeChild));
            var accountNumber = SelectText(acctFrom, "ACCTID");

            var transactions = new List<ParsedTransaction>();
            populateTransactions(stmt, transactions);
            sink.Add(new ParsedAccountStatement(institutionCode, accountNumber, transactions));
        }
    }

    private static Dictionary<string, string> BuildSecurityList(XmlDocument doc)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var secInfos = doc.SelectNodes("//SECLIST//SECINFO");
        if (secInfos is null) return map;

        foreach (XmlNode secInfo in secInfos)
        {
            var uniqueId = SelectText(secInfo, "SECID/UNIQUEID");
            var uniqueIdType = SelectText(secInfo, "SECID/UNIQUEIDTYPE");
            var ticker = SelectText(secInfo, "TICKER");
            if (string.IsNullOrWhiteSpace(uniqueId) || string.IsNullOrWhiteSpace(ticker)) continue;
            if (!string.Equals(uniqueIdType, "CUSIP", StringComparison.OrdinalIgnoreCase)) continue;

            map[uniqueId.Trim()] = ticker.Trim();
        }
        return map;
    }

    private static void ParseInvestmentTransactions(
        XmlNode invList,
        List<ParsedTransaction> transactions,
        IReadOnlyDictionary<string, string> securityList)
    {
        foreach (XmlNode node in invList.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;
            var tx = node.Name.ToUpperInvariant() switch
            {
                "BUYSTOCK" or "BUYMF" or "BUYOTHER" => InvBuySell(node, TransactionType.Buy, securityList),
                "SELLSTOCK" or "SELLMF" or "SELLOTHER" => InvBuySell(node, TransactionType.Sell, securityList),
                "INCOME" => InvIncome(node, securityList),
                "REINVEST" => InvReinvest(node, securityList),
                "TRANSFER" => InvTransfer(node, securityList),
                _ => null,
            };
            if (tx is not null) transactions.Add(tx);
        }
    }

    private static void ParseBankTransactions(XmlNode bankList, List<ParsedTransaction> transactions)
    {
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

    private static ParsedTransaction InvBuySell(
        XmlNode node,
        TransactionType type,
        IReadOnlyDictionary<string, string> securityList)
    {
        var invtran = node.SelectSingleNode(".//INVTRAN");
        var fitId = SelectText(invtran, "FITID") ?? string.Empty;
        var dtTrade = ParseDateOnly(SelectText(invtran, "DTTRADE"));
        var dtSettle = TryParseDateOnly(SelectText(invtran, "DTSETTLE"));
        var memo = SelectText(invtran, "MEMO");

        var (mappedTicker, mappedCusip) = ResolveSecurityId(
            SelectText(node, ".//SECID/UNIQUEID"),
            SelectText(node, ".//SECID/UNIQUEIDTYPE"),
            securityList);

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

    private static ParsedTransaction InvIncome(XmlNode node, IReadOnlyDictionary<string, string> securityList)
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

        var (mappedTicker, mappedCusip) = ResolveSecurityId(
            SelectText(node, ".//SECID/UNIQUEID"),
            SelectText(node, ".//SECID/UNIQUEIDTYPE"),
            securityList);

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

    private static ParsedTransaction InvReinvest(XmlNode node, IReadOnlyDictionary<string, string> securityList)
    {
        var invtran = node.SelectSingleNode(".//INVTRAN");
        var fitId = SelectText(invtran, "FITID") ?? string.Empty;
        var dtTrade = ParseDateOnly(SelectText(invtran, "DTTRADE"));
        var memo = SelectText(invtran, "MEMO");

        var (mappedTicker, mappedCusip) = ResolveSecurityId(
            SelectText(node, ".//SECID/UNIQUEID"),
            SelectText(node, ".//SECID/UNIQUEIDTYPE"),
            securityList);

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

    private static ParsedTransaction InvTransfer(XmlNode node, IReadOnlyDictionary<string, string> securityList)
    {
        var invtran = node.SelectSingleNode(".//INVTRAN");
        var fitId = SelectText(invtran, "FITID") ?? string.Empty;
        var dtTrade = ParseDateOnly(SelectText(invtran, "DTTRADE"));
        var memo = SelectText(invtran, "MEMO");

        var (mappedTicker, mappedCusip) = ResolveSecurityId(
            SelectText(node, ".//SECID/UNIQUEID"),
            SelectText(node, ".//SECID/UNIQUEIDTYPE"),
            securityList);

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

    private static (string? Ticker, string? Cusip) ResolveSecurityId(
        string? id,
        string? idType,
        IReadOnlyDictionary<string, string> securityList)
    {
        if (string.IsNullOrWhiteSpace(id)) return (null, null);
        var trimmed = id.Trim();
        var t = (idType ?? string.Empty).Trim().ToUpperInvariant();

        if (t == "TICKER") return (trimmed, null);

        var looksLikeCusip = t == "CUSIP" || (t.Length == 0 && trimmed.Length == 9);
        if (!looksLikeCusip) return (trimmed, null);

        var resolvedTicker = securityList.TryGetValue(trimmed, out var ticker) ? ticker : null;
        return (resolvedTicker, trimmed);
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

    private static string? NormalizeCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().ToLowerInvariant();
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
