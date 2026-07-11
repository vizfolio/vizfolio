# Performance API

The Performance API surfaces how a portfolio (or a single account inside it) performed over a date range. Read this doc when you're touching anything under `backend/src/Vizfolio.Application/Portfolios/` (the performance service and calculator strategies), the endpoints at `backend/src/Vizfolio.Api/Endpoints/Portfolios/GetPortfolioPerformanceEndpoint.cs` and `GetAccountPerformanceEndpoint.cs`, or the import parser pipeline that feeds them (`backend/src/Vizfolio.Application/PortfolioImports/`, see "Import pipeline & parser plugins" below).

## Endpoints

| Route | Verb | Handler |
|---|---|---|
| `/api/portfolios/{portfolioId}/performance` | GET | `GetPortfolioPerformanceEndpoint` |
| `/api/portfolios/{portfolioId}/accounts/{accountId}/performance` | GET | `GetAccountPerformanceEndpoint` |

Both accept two optional query parameters:

- `from` — start of period (inclusive). ISO date, e.g. `2025-06-01`. If omitted, defaults to the earliest `AccountTransaction.TradeDate` in scope, falling back to the earliest snapshot `AsOf`, falling back to today.
- `to` — end of period (inclusive). ISO date. If omitted, defaults to today (UTC).

Both endpoints return **404** if the portfolio or account doesn't exist (an account under a different portfolio also 404s), and **400** if the caller supplies `from > to`.

## Response shape

```jsonc
{
  "from": "2025-06-01",
  "to": "2026-06-01",
  "startingBalance": {
    "value": 0,
    "isComplete": false,
    "snapshotAsOf": null,
    "holdingsCovered": 0,
    "holdingsMissingSnapshot": 2
  },
  "endingBalance": {
    "value": 6255.00,
    "isComplete": true,
    "snapshotAsOf": "2026-06-01",
    "holdingsCovered": 2,
    "holdingsMissingSnapshot": 0
  },
  "contributions": {
    "net": 7500.00,
    "deposits": 7500.00,
    "withdrawals": 0,
    "count": 1
  },
  "returns": {
    "timeWeighted": {
      "rate": null,
      "method": "ModifiedDietz",
      "basis": "Period",
      "reason": "IncompleteStartingBalance"
    },
    "moneyWeighted": {
      "rate": null,
      "method": "XIRR",
      "basis": "Annualized",
      "reason": "IncompleteStartingBalance"
    }
  },
  "currencyCode": "USD"
}
```

## Balance basis: snapshot market value

Both `startingBalance.value` and `endingBalance.value` are computed the same way: **for each holding in scope, take the latest `AccountHoldingSnapshot.MarketValue` whose `AsOf ≤ date`, then sum**. Same unit at both ends of the period, always comparable.

This deliberately *does not* try to derive a balance by summing transaction amounts. Transaction sums don't include unrealized market movement and mix cash-flow units (Deposit $) with security-value units (MarketValue $), which produces meaningless numbers. The snapshot is the broker's ground truth; that's what we use.

### Completeness

`isComplete = true` iff every "relevant" holding had a snapshot at-or-before the date. A holding is **relevant** if it either (a) has a snapshot with `AsOf ≤ to`, or (b) has a transaction in `[from, to]`. Holdings that never existed in-window don't inflate the missing count.

`snapshotAsOf` is the latest contributing snapshot's date (null when no snapshot contributed). `holdingsCovered` and `holdingsMissingSnapshot` let the caller diagnose *why* a balance is incomplete without exposing per-holding detail.

**The canonical partial-history case** — user uploads one QFX with the last year of activity, so the only snapshot is at the end of period:

- `endingBalance` is fully populated (`isComplete: true`, snapshot from the QFX's `<DTASOF>`).
- `startingBalance.value` is `0` with `isComplete: false` and `holdingsMissingSnapshot > 0` — an honest "we don't know the starting balance." The fix is to upload an `OpeningBalance` or `Statement` snapshot at (or before) the `from` date.

## Contributions

`Contributions` is the sum of `AccountTransaction.Amount` for rows whose `Type ∈ {Deposit, Withdrawal, Transfer}` and `TradeDate ∈ [from, to]`. Other types (Buy, Sell, Dividend, Interest, Reinvest, CapitalGain, Fee, Split, Other) are internal rearrangements of value inside the account and are **not** contributions.

Sign convention on `Amount` (from the broker's perspective, as stored in the DB):
- Deposit: positive (cash in)
- Withdrawal: negative (cash out)
- Transfer: signed if it carries cash (bank `XFER`); zero if it's an in-kind investment `<TRANSFER>` (see QFX section below)

The response exposes:
- `net` — signed sum. Positive = money added to the portfolio, negative = money removed.
- `deposits` — sum of positive contributions.
- `withdrawals` — sum of negative contributions (kept negative).
- `count` — number of contributing rows.

### Portfolio-scope internal transfers

Contributions at portfolio scope sum every deposit/withdrawal/transfer across every account in the portfolio. An **internal transfer** between two accounts in the same portfolio (e.g., IRA rollover from Vanguard IRA → Fidelity IRA) shows up as a matched pair:

- Account A: Withdrawal `-$100`
- Account B: Deposit `+$100`

`net` correctly cancels to `0` — no money crossed the portfolio boundary. But `deposits` shows `+$100` and `withdrawals` shows `-$100`, which is misleading if consumed literally at portfolio scope.

**Convention**: UIs should headline `Contributions.Net` at portfolio scope and *not* surface `Deposits`/`Withdrawals` there. Gross figures are meaningful at *account* scope, where the transfer really is an inflow / outflow from that account's perspective. We deliberately do not detect and dedupe internal transfer pairs — Net already cancels correctly, and pair detection would require fuzzy heuristics that don't earn their keep at this stage.

## Returns

Two returns are reported, both under `Returns`:

- `timeWeighted` — period return, method `ModifiedDietz` (default). Reports how the portfolio grew after adjusting for the timing of cash flows.
- `moneyWeighted` — annualized IRR, method `XIRR`. Reports the annualized rate the investor experienced given their contribution timing.

Both are `decimal?` (null when uncomputable) with a `reason` string when null. `basis` is `"Period"` or `"Annualized"` so a caller can format correctly.

### Method: Modified Dietz (default TWRR)

```
R = (EMV − BMV − Σ Cᵢ) / (BMV + Σ (wᵢ · Cᵢ))
```

- `BMV` = `startingBalance.value`, `EMV` = `endingBalance.value`.
- `Cᵢ` = signed cash-flow amount at date `tᵢ`.
- `wᵢ = (T − dayFromStart) / T` — fraction of the period remaining after the flow.

Widely used by retail brokerages as a TWRR proxy. Formally a money-weighted approximation, so the `method` field says `ModifiedDietz` — consumers who need GIPS-grade TWRR can tell it apart from the strict chained calculator.

Reasons `rate` may be `null`:
- `IncompleteStartingBalance` / `IncompleteEndingBalance` — see the completeness section.
- `PeriodTooShort` — `from == to`.
- `ZeroDenominator` — `BMV = 0` with no offsetting weighted contributions.

### Method: ChainedSubPeriods (strict TWRR, opt-in)

Geometric chain of sub-period returns bounded by interior snapshot dates. Each sub-period return is computed with Modified Dietz to handle mid-sub-period flows; sub-period products give:

```
R = ∏ (1 + Rᵢ) − 1
```

Requires interior snapshots (i.e., snapshot `AsOf` strictly between `from` and `to`) where every relevant holding has coverage at that date. In the v1 QFX-only case (snapshot at end only), this calculator returns `null` with reason `InsufficientIntermediateSnapshots`.

To swap this in as the TWRR strategy, change one line in `backend/src/Vizfolio.Application/DependencyInjection.cs`:

```csharp
services.AddScoped<ITimeWeightedReturnCalculator, ChainedSubPeriodTimeWeightedReturnCalculator>();
```

### Method: XIRR (MWRR)

The true internal rate of return of the signed cash-flow stream, from the investor's perspective:

- `−StartingBalance` at `from` (outflow: money already invested)
- `−Amount` at each contribution's `TradeDate` (deposit into account = outflow from investor)
- `+EndingBalance` at `to` (inflow: value ultimately received)

Solves `Σ CFᵢ / (1 + r)^((tᵢ − t₀) / 365) = 0` via bisection over `r ∈ [−0.9999, 100.0]` with tolerance `1e-9` and 200 iterations. Bisection is chosen over Newton's for robustness — XIRR can have multiple roots for pathological flow patterns.

Reasons `rate` may be `null`:
- `IncompleteStartingBalance` / `IncompleteEndingBalance` — as above.
- `InsufficientCashFlows` — fewer than 2 non-zero consolidated flows.
- `NoSignChange` — all flows same sign; NPV is monotonic and never crosses zero.
- `DidNotConverge` — bisection endpoints failed to bracket a root.

## Currency

`currencyCode` is the mode of `CurrencyCode` values across contributing snapshots (ties broken by ordinal sort; defaults to `"USD"` when no snapshots have currency). This is a single reporting currency — multi-currency conversion is explicitly out of scope. If snapshots disagree, the caller sees the majority currency and no conversion is applied; the `value` fields will therefore mix currencies and the result should not be trusted for multi-currency portfolios today.

## Architecture: pluggable calculators

The performance service takes both calculators via DI:

```csharp
public sealed class PortfolioPerformanceService(
    IAppDbContext db,
    ITimeWeightedReturnCalculator twrCalculator,
    IMoneyWeightedReturnCalculator mwrCalculator)
```

Each calculator receives a `PerformanceComputationContext` — from/to dates, both balances plus their `IsComplete` flags, the ordered list of `CashFlow`s, and the interior `BalancePoint`s (built by walking every unique snapshot `AsOf` in `(from, to)` and computing the portfolio balance at that date; points where any relevant holding is missing coverage are dropped).

Adding a new return metric is a matter of implementing the corresponding interface and swapping the DI registration. Adding a *new* metric (e.g., drawdown, contribution-vs-market-effect decomposition) means adding a sibling record to `PerformanceReturnsResult` and a new field to the response — no changes to the route or existing metrics.

## Import pipeline & parser plugins

Broker files feed the ledger through a plug-in parser pipeline. Each format implements
`IPortfolioFileParser` (`backend/src/Vizfolio.Application/PortfolioImports/Abstractions/IPortfolioFileParser.cs`)
and is registered in `DependencyInjection.AddApplication`. `PortfolioImportService`
(`.../PortfolioImports/Services/PortfolioImportService.cs`) does the dispatch. Parsers ship today:
`QfxFileParser` (OFX/QFX, `SourceSystem="QFX"`) and `VanguardTransactionHistoryReportParser`
(the per-account "Create a Report" `.xlsx`, `SourceSystem="VANGUARD"`, read with ClosedXML — MIT).

The parser contract carries selection metadata beyond `SourceSystem`:

- **`DisplayName`** — human label shown in the UI "Format" dropdown.
- **`Priority`** — auto-detect offers parsers highest-first, so provider-specific parsers sit above
  any generic fallback (QFX = 100, Vanguard = 200). Ties fall back to registration order.
- **`FileExtensions`** — powers the UI `accept` hint (aggregated across parsers).

**Selection.** By default the format is **auto-detected**: the buffered upload is offered to each
parser's `CanParseAsync` in priority order and the first match wins (415 `UnsupportedFormat` if none
claim it). Callers may **force** a parser by passing `sourceSystem` (the `SourceSystem` key) on the
import endpoints — this skips detection; an unregistered key returns 415 `UnknownParser`. The UI
happy path sends no override (0 extra clicks); the dropdown defaults to *Auto-detect* and only sends
`sourceSystem` when the user overrides.

**Discovery.** `GET /api/imports/parsers` returns `[{ sourceSystem, displayName, fileExtensions }]`
(priority order) so the UI can build the dropdown and the file-picker `accept` list.

**Dedup is per source system.** Transactions dedup on `(account, sourceSystem, externalId)`. So the
same holding imported from both the QFX export and the Vanguard report (which overlap on the recent
~18 months) will **not** cross-dedup — an open item to resolve when the Vanguard row parser lands
(`ParseAsync` is currently a stub; detection is live).

### Adding a provider parser

1. Implement `IPortfolioFileParser` (populate the shared `ParsedTransaction`/`ParsedPosition` records —
   no new parsed models needed).
2. Register it in `DependencyInjection.AddApplication`.
3. Set `Priority` above any generic parser and give it distinctive `CanParseAsync` signature detection.

That's the whole extension surface — the endpoints, discovery, dedup, and UI dropdown pick it up
automatically.

## QFX ingestion nuances

The performance numbers are only as good as the transactions and snapshots imported from QFX. The parser at `backend/src/Vizfolio.Application/PortfolioImports/Parsers/QfxFileParser.cs` has three important behaviors that affect performance results:

### Cash contributions live in `<INVBANKTRAN>` inside `<INVSTMTRS>`

OFX 2.x brokerage exports wrap cash movements (ACH deposits, withdrawals, cash sweeps) in `<INVBANKTRAN>` **inside** `<INVSTMTRS>/<INVTRANLIST>`, alongside `<BUYSTOCK>`, `<SELLSTOCK>`, `<INCOME>`, `<REINVEST>`, `<TRANSFER>`. The parser recognizes `INVBANKTRAN`, unwraps its child `<STMTTRN>`, and reuses the same TRNTYPE-to-`TransactionType` mapping used for bank statements. `<SUBACCTFUND>` (typically `CASH`) is ignored.

Without this handling, brokerage-account deposits and withdrawals would be silently dropped and `Contributions` would sum to `0` even when the QFX contained obvious cash movements — the exact bug that occurred prior to 2026-06-30.

### `<TRANSFER>` inside `<INVTRANLIST>` stores `Amount = 0`

`InvTransfer` in the parser captures `UNITS` (share quantity) but hardcodes `Amount = 0m` — an in-kind investment transfer (ACAT of shares) has no cash amount in the QFX. This means an ACAT of $100k of shares into a brokerage account **does not** appear as a contribution. Only bank-style cash transfers (via `<INVBANKTRAN>` with `TRNTYPE=XFER`, or `<STMTRS>/<BANKTRANLIST>` with `TRNTYPE=XFER`) carry a non-zero `Amount`.

If cost basis for the transferred shares matters for performance, upload an `AccountHoldingSnapshot` with source `Statement` at the transfer date so the incoming position is reflected in the starting balance for periods that begin after the transfer.

### `<INVBAL>` cash balance is not parsed

`<INVBAL>` inside `<INVSTMTRS>` carries the account's cash balance at `DTASOF`. The current parser does not read it, so `AccountHoldingSnapshot` records represent *securities market value only*. For accounts with meaningful cash positions, the reported balance understates portfolio value by the cash amount. This is an explicit v1 limitation, not a bug in the performance service.

## Reconciliation (future work)

Snapshots and transactions describe the same account from two angles, and it's tempting to want the service to *reject* a snapshot that doesn't tie out against the ledger. In practice we deliberately don't, because two invariants behave differently:

- **Quantity is a hard invariant.** For any holding, `expectedQtyAtB = qtyAtA + Σ (Buy.qty − Sell.qty + Reinvest.qty + Transfer.qty) over (A, B]`. When `snapshot.Quantity` disagrees with `expectedQty`, the ledger is genuinely wrong — a transaction is missing, duplicated, or a corporate action (split, spin-off) wasn't captured. This is actionable, and the fix belongs in the ledger.
- **Market value is a soft invariant.** `actualValueDelta − Σ contributions − Σ income` equals *implied market movement* (unrealized gain/loss + reinvested dividends at unknown prices + FX + fees not otherwise captured). That number is never zero for a snapshot pair spanning real time, because market movement is by definition not in the transaction ledger. There is no threshold at which "tied out" is a defensible cutoff.

Rejecting a snapshot on the strict value delta would therefore reject *every honest snapshot*. Rejecting on quantity mismatch would block partial-history users (whose earlier transactions aren't imported yet) from ever recording an opening balance.

The right posture is **accept always, report separately**. A future `GET /api/portfolios/{id}/accounts/{accountId}/reconciliation` endpoint would walk adjacent snapshot pairs and, for each holding, return findings labeled:

- `QuantityMismatch` — hard, actionable. `expectedQty ≠ snapshot.Quantity`.
- `LargeValueDelta` — soft, informational. `|impliedMarketMovement|` exceeds a configurable ratio of the prior balance (default: 3× — twenty-five hundred percent between two snapshots is a real signal, five percent is not).

The endpoint would be report-only; imports and `POST .../opening-balance` continue to succeed regardless. The UI decides whether to render findings quietly on a data-hygiene screen or loudly on onboarding, and could later persist per-finding acknowledgements.

Nothing is built for this yet. Current behavior: any snapshot is accepted at face value, and any transaction ledger is used as-is. When this becomes a real UX requirement — most likely once the app has enough long-term users to accumulate quantity drift — this is the section to expand into an actual endpoint.

## Extending the response

New sibling metrics slot onto `PerformanceReturnsResult` or as top-level fields on `PortfolioPerformanceResult`. Route and existing metrics don't change. The mapping layer (`backend/src/Vizfolio.Api/Endpoints/Portfolios/PerformanceMapping.cs`) projects Application-layer records into API records; add a new mapping method there when the shape grows.

The service currently issues one round-trip per data set (holdings, snapshots, active-in-range holdings, contribution rows, default-`from` resolution). Adding another metric that needs the same data should reuse the loaded lists rather than requerying.
