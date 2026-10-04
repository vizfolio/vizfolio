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

**Period boundaries.** `startingBalance` is valued at the **start of `from`** — the close of the day
before — and `endingBalance` at the close of `to`; every cash flow dated in `[from, to]` is inside the
period. So a purchase on `from` (and the contribution that funded it) is a flow, never also part of the
starting balance. This lines up with opening balances, which are entered as of the day *before* the first
transaction (the default `from`): that snapshot is exactly the starting balance.

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
  "currencyCode": "USD",
  "series": {
    "interval": "Monthly",
    "points": [
      { "date": "2025-05-31", "value": 0,       "deposits": 0,       "withdrawals": 0, "cumulativeReturn": 0,       "investmentGain": 0 },
      { "date": "2025-06-30", "value": 7410.25, "deposits": 7500.00, "withdrawals": 0, "cumulativeReturn": -0.0137, "investmentGain": -89.75 },
      // … one point per month end …
      { "date": "2026-06-01", "value": 6255.00, "deposits": 0,       "withdrawals": 0, "cumulativeReturn": null,    "investmentGain": null }
    ]
  }
}
```

## Value / returns-over-time series

`series` backs the "Value over time" and "Investment returns over time" charts. `PerformanceSeriesBuilder`
produces:

- **An opening point** at the close of the day before `from` — the starting balance — then **one point per
  interval end**, the last clamped to `to` (the ending balance).
- **Interval** chosen from the period's length: `Weekly` up to 92 days, `Monthly` up to 10 years,
  `Quarterly` beyond — so even a 15-year history is ~60 points.
- **`value`** from the same resolver as the balances (`ComputeBalance`: PriceHistory, snapshot fallback,
  not-held `$0`), so the chart always agrees with the headline numbers. `null` when any relevant holding
  is missing a valuation at that date — drawn as a gap, never guessed.
- **`deposits` / `withdrawals`** (withdrawals negative): the contribution cash flows in
  `(previous point, point]`, so across all points they sum to `contributions`.
- **`cumulativeReturn`** (decimal rate): the return from `from` to the point. The service runs the
  **configured `ITimeWeightedReturnCalculator`** over `[from, point]` — the headline context `with` `To`,
  the ending balance, cash flows ≤ point and intermediate balances < point — so the last point always equals
  `returns.timeWeighted.rate`, whichever strategy is registered. The builder takes this as a delegate and
  stays calculator-agnostic. `null` where the calculator returns no rate (e.g. `PeriodTooShort`,
  `ZeroDenominator`), which the chart draws as a gap.
- **`investmentGain`**: `value − starting balance − net contributions to date`, i.e. the change in value
  not explained by deposits/withdrawals. The last point equals `ending − starting − contributions.net`.
- Both are `0` at the opening point, and `null` when the point or the opening couldn't be fully valued.

Cost is one balance plus one return calculation per point over data already loaded for the response —
no extra queries.

## Balance basis: holdings and cash, valued by the account engine

Both `startingBalance.value` and `endingBalance.value` are the sum, over the in-scope accounts, of each account's
value at the date: **every holding's shares × price, plus the account's cash**. One `AccountStateEngine` per account
(`Application/Portfolios/Valuation/`, built by `AccountValuationLoader`) does all valuation — performance, the
Holdings tab, implied contributions and the opening-positions screen share it. Full design:
[price-history-valuation.md §11](./price-history-valuation.md#11-account-valuation-engine-phase-4).

**Shares** roll forward from known positions — stored snapshots and the **derived opening** (the earliest broker
statement rolled back over the ledger to the day before the account's first transaction, so a partial history
starts from what the account really held). Provider splits apply on their ex-date. Per holding per date:

0. **Fresh broker snapshot** — the latest valued snapshot with `AsOf ≤ date` is used as-is when no share-affecting
   trade follows it and no price is newer than it. Keeps Performance consistent with the Holdings tab.
1. **PriceHistory** — `quantity × close` when a raw close exists on/before the date and is no more than
   `Valuation:MaxPriceAgeDays` (default 10) days old. A **stable-NAV (money-market) fund** is valued at
   **`quantity × its stable price`** (usually $1.00; some funds use $10 or $100) when there's no recent close —
   the price the fund states in its SEC Form N-MFP, via the SEC money market registry (`MoneyMarketFund`, from fund-extracts' `money_market_funds.json` — see [fund-data.md](./fund-data.md)); a ticker it doesn't cover qualifies if its
   price history never moves over ≥ 20 closes (`StablePrices`). Floating-NAV money market funds are not assumed
   to be worth $1.
2. **Broker snapshot** — otherwise the latest snapshot's value, revalued at its per-share price if shares changed
   since. A snapshot recording **nothing held** (0 shares, $0) never values a position the ledger says is held.
3. **Not held → $0, complete.**
4. **Missing** — held but unvalued (honest null downstream). The cause is reported (see Completeness).

**Cash** is uninvested cash plus the **settlement fund** — recognised from the account's own history, not a list:
a ticker on a "Sweep…" row, or a money market fund (per the registry) whose movements come without share counts or
that appears only on statements (`SettlementFunds`): its shares *are* the cash, so it isn't valued as an investment. Cash rolls by each row's cash effect
on its trade date (deposits, withdrawals, buys, sells, cash income, fees; sweeps and settlement-fund trades are
neutral; reinvestments are funded by their income) and is anchored on the settlement fund's (and any `$CASH`
holding's) snapshots. On real multi-year Vanguard data the cash roll lands within a few dollars of the settlement-fund balances.

This deliberately *does not* derive a balance by summing transaction amounts alone: shares are valued at market.

### Completeness

`isComplete = true` iff nothing in scope — no holding and no account's cash — resolved to **missing**. Every holding
of the in-scope accounts is evaluated. `holdingsMissingSnapshot` counts the missing components (the name predates
price-history valuation); **`missing`** lists up to 10 of them — `{ accountId, accountHoldingId, symbol, cause }`,
a null symbol being the account's cash — so a UI can say *why*:

| `cause` | Meaning |
|---|---|
| `NoPrice` | Held, but no close (or statement) on or near the date. |
| `StalePrice` | The only close is older than `MaxPriceAgeDays`. |
| `NegativePosition` | The ledger sells/transfers out more than it holds — history is missing rows. |
| `MaterialMismatch` | The ledger disagrees materially with the broker's next statement (see below), so it's not trusted until that statement. |
| `BeforeHistory` | The date is before the account's imported history and a position was already held then. |
| `PricesPending` | `NoPrice`/`StalePrice`, but a background price fetch for the account is queued or running (e.g. right after an import): the UI says "Updating…" and refreshes until the prices arrive. |

The Holdings endpoint reports the same cause per row as `missingCause` (null unless `status` is `Missing`).

`snapshotAsOf` is the latest contributing snapshot's date (null when none contributed). `holdingsCovered` counts
valued components, cash included.

**Reconciliation and materiality.** At each broker statement the engine compares the rolled quantity (or cash) with
the broker's and resets to the broker's. A drift worth more than `max(Valuation:MismatchMaterialityMin` ($10),
`MismatchMaterialityPct` (0.5%) × the account's value)` makes that component `MaterialMismatch` between the
previous statement and this one; a smaller drift is only recorded (findings, surfaced by Data Health later).

**The canonical partial-history case** — one QFX with the last year of activity, its statement at the end:

- `endingBalance` is complete (the statement anchors every position and the settlement fund).
- At the default `from` (the first transaction), the starting balance is the **derived opening**: positions held
  before the history are valued from PriceHistory at the day before; nothing has to be entered by hand.
- For a `from` *before* the imported history, a position held before it is `BeforeHistory` (unknown), not $0.

## Contributions


`Contributions` are the account engine's **external flows** in `[from, to]`: rows whose `Type ∈ {Deposit, Withdrawal, Transfer}` (implied contributions included), at their `Amount`. An **in-kind transfer** of a security (a `Transfer` with a quantity) moves shares across the boundary: Vanguard reports its value as the amount; a QFX `<TRANSFER>` reports none, so it's valued at the day's close × quantity (falling back to the broker's `UNITPRICE`, then the latest statement's per-share price). If none exists the flow is unvalued and both returns are `null` with reason **`UnvaluedTransfer`**. Other types (Buy, Sell, Dividend, Interest, Reinvest, CapitalGain, Fee, Split, ReturnOfCapital, Journal, Other) are internal rearrangements of value inside the account and are **not** contributions. A `ReturnOfCapital` (OFX `RETOFCAP`) is cash in that is neither income nor a contribution; a `Journal` (OFX `JRNLSEC`/`JRNLFUND`) moves shares or cash between the account's own sub-accounts and changes nothing at account level.

Sign convention on `Amount` (from the broker's perspective, as stored in the DB):
- Deposit: positive (cash in)
- Withdrawal: negative (cash out)
- Transfer: signed if it carries cash (bank `XFER`); an in-kind QFX `<TRANSFER>` stores `0` and is valued at the day's price (above); its share direction comes from `TFERACTION`

The response exposes:
- `net` — signed sum. Positive = money added to the portfolio, negative = money removed.
- `deposits` — sum of positive contributions.
- `withdrawals` — sum of negative contributions (kept negative).
- `count` — number of contributing rows.

### Implied contributions

Older fund-company (pre-brokerage) accounts record a contribution *as a purchase*: the history shows
the Buy but no deposit, so the money appears from nowhere and inflates the return. Vizfolio detects
these and records them as ledger rows, so contributions and both returns account for the money.

`ImpliedContributionCalculator` rolls the account's cash forward from zero (or from the cash held before a partial
history — see "Cash held before a partial history" below), one day at a time by
**settlement date** (trade date when a row has none) — cash moves at settlement, and the money funding a
purchase is often recorded on a later date than the purchase's trade date (row order within a day
doesn't matter), using each row's cash effect regardless of the broker's sign
conventions: Deposit/Sell/cash income `+`; Withdrawal/Buy/Reinvest/Fee `−`; income carrying a quantity
(reinvested at source) and in-kind transfers (ticker + quantity) `0`; cash transfers and other
unrecognised activity as signed. The **settlement fund counts as cash**: it's recognised as any ticker
on a "Sweep" row, and moving money into or out of it (buy/sell/reinvest/sweep) is `0` — only its income
adds cash. That also stops a sweep recorded by two sources (a QFX *Buy* of the fund and a Vanguard
report *Sweep in*, which dedup can't pair because only one carries a quantity) from being spent twice.
An **older fund-company account records new money as a purchase of the settlement fund itself** — a `Buy`
of the money-market fund with a **positive amount** (that era's sign for every purchase) and no deposit row.
That is money from outside, so it spends cash like any purchase and surfaces as an implied contribution; a
sweep into the fund from cash is reported with a negative amount (or no quantity) and stays neutral.
**Reinvestments are funded by the income they reinvest**: the Vanguard report lists that income as
its own row (Dividend `+`, Reinvestment `−`), but a QFX `REINVEST` folds it into the one row, so the
part of a day's reinvestments not covered by that day's cash income is added back (per day, not per
ticker — some report dividend rows have no ticker). A reinvestment on its own can never imply a
contribution. A day that closes more than $1 below zero implies a contribution
of the shortfall, and cash resets to zero. The response lists each implied contribution with the rows
that caused it (and their cash effect), totals by year, and the ledger-implied `endingCash`.

**Stored at import.** After every import, `PortfolioImportService` calls
`ImpliedContributionService.SyncForAccountAsync` for each affected account. It recomputes from the
account's *imported* rows and stores the result as `Deposit` rows with `SourceSystem = "VIZFOLIO"`,
`SourceType = "Implied contribution"` and `ExternalId = "implied-{date}"` (at most one per day). Rows
that are still implied are kept as-is; the rest are removed — so a later import that brings in the
real deposit retires the implied one, and re-importing never duplicates them. Because they're ordinary
deposits they need no special handling in contributions, TWR or XIRR, and they appear in the Ledger.

- **Never used for dedup.** Import dedup ignores `VIZFOLIO` rows, so a real deposit with the same
  date and amount as an implied one is imported (then the sync retires the implied row). The
  calculator also ignores them, so derived rows never feed back in.
- **Reported on the import result**: `AccountImportResult.ImpliedContributions` /
  `ImpliedContributionsAmount` (account totals after the import); the UI shows a note beneath the
  import summary.
- **Opening balances.** Enter positions held *before* the first transaction (the suggested
  day-before date, usually `$0`). Entering an unfunded first purchase as an opening balance on its
  own date would now count that money twice — as starting balance and as an implied contribution.
- **Dry run.** `GET /api/portfolios/{portfolioId}/accounts/{accountId}/implied-contributions` shows
  what the calculator derives (with each day's rows and their cash effect, totals by year, and the
  ledger-implied `endingCash`) without writing anything — useful for checking against a broker's
  contribution history.

#### Broker assumptions (read before adding a parser)

The mechanism is broker-agnostic (it works on normalized `TransactionType`s), but it has only been
validated against Vanguard (the transaction report plus QFX). Three rules encode Vanguard conventions:

- **Settlement fund = a ticker the parser marked as one.** Parsers set `ParsedTransaction.IsSettlementFund` /
  `ParsedPosition.IsSettlementFund` from per-broker knowledge (the [broker profiles](#broker-profiles) for QFX; the
  Vanguard report marks its "Sweep…" rows), stored as `AccountTransaction.IsSettlementFund` and
  `AccountHolding.IsSettlementFund`. Rows stored before parsers marked them are still recognised by a `SourceType`
  starting with "Sweep" (`ImpliedContributionCalculator.SettlementTickers`, `SettlementFunds`). For *valuation*,
  `SettlementFunds` also treats a money market fund (per the SEC registry) as cash when its own rows can't be rolled.
  A broker without a profile marks nothing, so its money-market fund is an ordinary fund — still correct when both
  legs of each move are in the file.
- **`Other` rows count at their reported sign** — right for Vanguard sweeps; unknown elsewhere. In the
  quantity roll-forward, an `Other` row that **carries a quantity** moves shares by its sign (Vanguard's old
  "Sweep" rows move money-market shares out to pay for other purchases); without a quantity it moves none.
- **A positive-amount `Buy` of the settlement fund is new money** (pre-2016 Vanguard fund-company accounts).
- **Income rows carrying a quantity are reinvested at source** — a pre-2018 Vanguard convention.

#### Cash held before a partial history

**Handled.** The main false-positive risk was cash held before the imported history (e.g. an 18-month QFX for
an account that already held cash): purchases paid from it looked unfunded. The roll is now **seeded with the
derived opening cash** (`AccountStateEngine.OpeningCashSeed`): when any position predates the history, the earliest
settlement-fund/cash statement rolled back over the *imported* rows (never Vizfolio's own implied rows) gives the
cash already held. With a complete history (nothing held before it) the seed is $0 and unfunded purchases are
implied as before — the two cases can't be told apart from cash alone, so the share positions decide (roadmap
Appendix A.2). A user can also enter cash directly as an opening balance (symbol `$CASH`).

A QFX statement's available cash (`<INVBAL><AVAILCASH>`) anchors cash too, at brokers that hold it outside a
settlement fund (see [`<INVBAL>`](#invbal-available-cash-anchors-the-accounts-cash)).

Remaining:
1. **Per-account switch** (`Account.InferContributions`) to turn inference off.
2. **Validate each new broker with real exports** (anonymized fixtures, dry-run preview on complete histories).

### Portfolio-scope internal transfers

Contributions at portfolio scope sum every deposit/withdrawal/transfer across every account in the portfolio. An **internal transfer** between two accounts in the same portfolio (e.g., IRA rollover from Vanguard IRA → Fidelity IRA) shows up as a matched pair:

- Account A: Withdrawal `-$100`
- Account B: Deposit `+$100`

`net` correctly cancels to `0` — no money crossed the portfolio boundary. But `deposits` shows `+$100` and `withdrawals` shows `-$100`, which is misleading if consumed literally at portfolio scope.

**Convention**: UIs should headline `Contributions.Net` at portfolio scope and *not* surface `Deposits`/`Withdrawals` there. Gross figures are meaningful at *account* scope, where the transfer really is an inflow / outflow from that account's perspective. We deliberately do not detect and dedupe internal transfer pairs — Net already cancels correctly, and pair detection would require fuzzy heuristics that don't earn their keep at this stage.

## Returns

Two returns are reported, both under `Returns`:

- `timeWeighted` — period return, method `ModifiedDietz` (default). Reports how the portfolio grew after adjusting for the timing of cash flows.
- `moneyWeighted` — IRR, method `XIRR`. Reports the rate the investor experienced given their contribution timing: **annualized** for periods of a year or more, the **period** rate for shorter ones.

Both are `decimal?` (null when uncomputable) with a `reason` string when null. `basis` is `"Period"` or `"Annualized"` so a caller can format correctly.

### Method: Modified Dietz (default TWRR)

```
R = (EMV − BMV − Σ Cᵢ) / (BMV + Σ (wᵢ · Cᵢ))
```

- `BMV` = `startingBalance.value`, `EMV` = `endingBalance.value`.
- `Cᵢ` = signed cash-flow amount at date `tᵢ`.
- `wᵢ = (T − dayFromStart) / T` — fraction of the period remaining after the flow.

Widely used by retail brokerages as a TWRR proxy. Formally a money-weighted approximation, so the `method` field says `ModifiedDietz` — consumers who need GIPS-grade TWRR can tell it apart from the strict chained calculator.

**Known limitation — long periods with large flows.** Applied as one period over many years, the denominator's weighting treats a large withdrawal as absent for the rest of the window, shrinking the "average invested capital" and inflating the rate. E.g. an account mostly withdrawn years before `to` (then only brief in-and-out round trips) can report a markedly higher rate than a brokerage's chained time-weighted figure, even with identical contributions and ending balance. The fix is a chained TWR that values the account at every external cash-flow date (from PriceHistory) — not yet built; it also depends on a raw price series (see [price-history-valuation.md §5](./price-history-valuation.md#5-gotchas-to-carry-forward)).

Reasons `rate` may be `null`:
- `IncompleteStartingBalance` / `IncompleteEndingBalance` — see the completeness section.
- `PeriodTooShort` — `from == to`.
- `ZeroDenominator` — `BMV = 0` with no offsetting weighted contributions.

### Method: ChainedSubPeriods (strict TWRR, opt-in)

Geometric chain of sub-period returns bounded by interior snapshot dates. Each sub-period return is computed with Modified Dietz to handle mid-sub-period flows; sub-period products give:

```
R = ∏ (1 + Rᵢ) − 1
```

The first sub-period opens at the start of `from` (the starting balance is the close of the day before), so it owns flows dated `from`; later sub-periods own flows after their opening boundary. Requires interior snapshots (i.e., snapshot `AsOf` strictly between `from` and `to`) where every relevant holding has coverage at that date. In the v1 QFX-only case (snapshot at end only), this calculator returns `null` with reason `InsufficientIntermediateSnapshots`.

To swap this in as the TWRR strategy, change one line in `backend/src/Vizfolio.Application/DependencyInjection.cs`:

```csharp
services.AddScoped<ITimeWeightedReturnCalculator, ChainedSubPeriodTimeWeightedReturnCalculator>();
```

### Method: XIRR (MWRR)

The true internal rate of return of the signed cash-flow stream, from the investor's perspective:

- `−StartingBalance` at `from` (outflow: money already invested)
- `−Amount` at each contribution's `TradeDate` (deposit into account = outflow from investor)
- `+EndingBalance` at `to` (inflow: value ultimately received)

Solves `Σ CFᵢ / (1 + r)^((tᵢ − t₀) / U) = 0` via bisection over `r ∈ [−0.9999, 100.0]` with tolerance `1e-9` and 200 iterations. Bisection is chosen over Newton's for robustness — XIRR can have multiple roots for pathological flow patterns.

**Basis.** When `to − from ≥ 365` days, `U = 365` and `basis = "Annualized"`. For a shorter period, `U` is the flows' own span (first to last flow) and `basis = "Period"`: `r` is then the return over the period itself. Annualizing a few weeks' gain misleads (3% in two weeks reads as ~115% a year), and solving in period units keeps a short, large move inside the solver's bounds.

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

**Dedup is content-based and cross-source.** Because formats overlap (the Vanguard report and the QFX
export share the recent ~18 months) and carry no common transaction id, dedup keys on a
**`TransactionFingerprint`** (`.../PortfolioImports/Services/TransactionFingerprint.cs`): a hash of
`account | tradeDate | symbol | signed quantity | amount`, rounded to absorb representation
noise. It deliberately **excludes** the source system and the (normalized) `TransactionType` — the type
is the field most likely to diverge across sources and would defeat the match. Direction comes from the
**quantity's sign** for share rows (buy/reinvest `+`, sell `−`), so their amount is compared by
**magnitude** — brokers disagree on that sign (QFX reports a reinvestment's total as positive, the
Vanguard report as negative, which otherwise imported every overlapping reinvestment twice). Cash-only
rows (no quantity) keep the **signed** amount, since it's all that separates a deposit from a withdrawal.

On import, `LedgerMatcher` (`.../PortfolioImports/Services/LedgerMatcher.cs`) loads the account's existing rows
across **all** sources. An incoming row is a duplicate when (1) a row from the **same source** has its
`ExternalId`, or else (2) an unclaimed existing row from **any** source has its fingerprint (a fingerprint
**multiset**: each stored row covers one incoming row at most) — so a trade already imported from QFX is not
re-imported from the Vanguard report, while N genuine same-day duplicates are preserved. A row matched by id also
uses up its fingerprint, so it can't absorb a second identical row too.

`ExternalId` stays the unique/index key: QFX uses `FITID`; id-less formats synthesize
**`"{fingerprint}-{occurrence}"`** — the n-th identical row in the file. The same transaction therefore gets the
**same id in every export** (the basis for future user corrections keyed by `(AccountId, SourceSystem, ExternalId)`),
genuine duplicates stay storable, and a later export with one more identical row adds just that row. Rows stored
under the old `"{fingerprint}-{rowOrdinal}"` scheme are rewritten deterministically at startup by
`StableExternalIdMigration` (per account, source and fingerprint, ordered by import time then old suffix; idempotent,
run by `ImportMaintenanceHostedService`).

Residual edges (accepted): a Deposit vs an inbound Transfer of the identical amount on the same day can't
be told apart without an id; QFX records in-kind `<TRANSFER>` with `Amount = 0` while Vanguard transfers
carry a real amount, so those specific rows won't cross-dedup (rare in the overlap).

**`SourceType` — preserving the raw label.** `AccountTransaction.SourceType` (nullable) stores the
broker's verbatim type next to the normalized `TransactionType`, so nothing is lost when a messy label is
mapped. The Vanguard parser maps its types as follows (raw kept in `SourceType`):

| Raw Vanguard `Type` | `TransactionType` |
|---|---|
| `Buy`, `Buy (exchange)` | `Buy` |
| `Sell`, `Sell (exchange)` | `Sell` |
| `Dividend` | `Dividend` |
| `Capital gain (ST\|LT)` | `CapitalGain` |
| `Reinvestment`, `Reinvestment (LT gain)`, `Reinvestment (ST gain)` (any `Reinvestment …`) | `Reinvest` |
| `Interest` | `Interest` |
| `Fee` | `Fee` |
| `Funds Received`, `Contribution` | `Deposit` (external in; `Contribution` = IRA contribution) |
| `Transfer (incoming)`, `TRANSFER FROM …` | `Transfer` (money/shares in) |
| `TRANSFER TO …` | `Transfer` (money/shares out) |
| `Conversion (incoming)` / `Conversion (outgoing)` | `Transfer` in / out (IRA conversion, e.g. Traditional → Roth) |
| `Share Conversion (incoming)` / `Share Conversion (outgoing)` | `Buy` / `Sell` (share-class exchange, e.g. Investor → Admiral) |
| `Conversion` (no direction — older reports) | `Buy` if quantity > 0, `Sell` if < 0 (share-class exchange); `Other` without a quantity |
| `Sweep`, `Sweep in`, `Sweep out` | `Other`, marked `IsSettlementFund` |

The Vanguard `Amount` reflects settlement-fund mechanics, so the parser sets the **sign of the external
cash types from the label**, not the reported amount: incoming transfers/conversions → `+|amount|`,
`TRANSFER TO …`/`Conversion (outgoing)` → `−|amount|`. A share-class conversion swaps one fund for
another *inside* the account, so it moves shares but is not a contribution. Sweeps → `Other` keeps
internal money-market cash a no-op for `Contributions` (which only sum `{Deposit, Withdrawal, Transfer}`).
Any unrecognised label also maps to `Other` (cash at its reported sign, shares only with a quantity) **with an
`UnmappedLabel` import warning** naming the label, so a label that needs mapping is visible right after the upload.
A row with no readable date is skipped with a `RowFailed` warning instead of failing the file.

Older Vanguard reports (pre-2018) record a reinvested distribution as a single `Dividend` /
`Capital gain` row **carrying the shares bought**; the quantity roll-forward counts those shares (newer
reports use a separate `Reinvestment` row). Sold units are reported negative; the roll-forward treats
any `Sell` as reducing the position regardless of sign.

Re-importing **does** correct rows already stored when the parser now maps them differently (roadmap Appendix
A.11): a row from the **same source** matched by `(SourceSystem, ExternalId)` or by fingerprint has its
import-owned fields — `Type`, `Amount`, `Quantity`, `Price`, `SettlementDate`, `SourceType`, `IsSettlementFund`
(`ImportedTransactionFields`) — brought in line, counted in `AccountImportResult.Updated` (also in `Skipped`), and
recorded as an `ImportBatchRowUpdate` (values before and after) so undoing the import restores them. Rows from a
*different* source are never touched — sources legitimately label the same event differently. Only the import
pipeline writes these fields; user corrections will live in a separate overlay. After a parser fix, **reprocess**
the stored files (below) or upload a newer export; no clearing is needed.

### Import batches: provenance, re-uploads, reprocess and undo

Every upload is an **`ImportBatch`**: the file name, its SHA-256, the parser, when, the **file itself (gzip)**, a
JSON summary (per-account counts + warnings) and a status (`Active`/`Undone`). Rows and snapshots it inserts carry
`ImportBatchId`; accounts and holdings it creates carry `CreatedByImportBatchId`. These are plain indexed columns,
not FKs (a second cascade path from the portfolio is rejected by SQL Server; batches are never deleted).

- **Same file again.** An upload whose hash matches an active batch of the same portfolio (and account, for
  account-scoped uploads) writes nothing and returns `status: AlreadyImported` (7) with that batch's id, date,
  counts and warnings. A *different* file with overlapping rows is deduped row by row as above.
- **Warnings.** Whatever the parser didn't fully understand comes back as `warnings: [{ code, message, count,
  samples }]` (codes in `ImportWarningCodes`: `UnmappedLabel`, `UnknownAggregate`, `RowFailed`,
  `PositionUnresolved`, `SplitWithoutRatio`, `UnsupportedSecurityId`, `OptionActivity`, `MarginBalance`,
  `OtherAccountSkipped`) and is kept on the batch. Nothing is dropped without one.
- **Atomic.** Each import (rows, snapshots, relink, implied contributions, summary) runs in one database
  transaction (`IAppDbContext.ExecuteInTransactionAsync`).
- **`POST /api/imports/reprocess[?portfolioId=]`** re-parses every active stored file, oldest first, with the parser
  that imported it: rows a parser used to drop are added (tagged with their original batch), rows it now maps
  differently are updated (recorded on that batch). Returns `{ batches, inserted, updated, snapshotsInserted,
  perBatch[] }`; a batch whose parser is gone is listed with `skipped`. In the UI: **Settings → Ledger → Reprocess
  imports** (all portfolios). Imports from before batches existed have no
  stored file: re-upload them once.
- **`GET /api/portfolios/{id}/imports`** lists the batches newest first (`fileName`, `sourceSystem`, `importedAt`,
  `reprocessedAt`, `status`, `undoneAt`, per-account counts with the account's current name, `warnings`) plus
  `transactionsImportedBeforeHistory` — rows that belong to no batch and can't be undone.
- **`GET /api/portfolios/{id}/imports/{batchId}/undo-preview`** / **`POST .../undo`** (no body). Undo, in one
  transaction: restores rows the batch updated (only where they still hold its values — a later import's change
  stands); deletes the rows and snapshots it inserted (never rows it skipped as duplicates, never pre-batch rows);
  removes holdings, then accounts, it created that nothing else uses; marks it `Undone`; **replays later batches
  into the same accounts** from their stored files, so rows they skipped as duplicates of the undone file come back;
  then relinks and re-derives implied contributions. Both return `{ transactions, snapshots, updatesReverted,
  holdingsRemoved, accountsRemoved, laterImportsReplayed[], laterImportsWithoutFile[] }`; `404` unknown, `409`
  already undone. An undone file can be imported again.

### Single-account files, shared statements and account numbers

- **A QFX on an account's Import tab** imports only the statement whose `ACCTID` matches the account; the file's
  other statements are skipped with an `OtherAccountSkipped` warning (masked number, e.g. `…1234`). If none match,
  the upload is rejected with **422 `AccountMismatch`** (8) and `fileAccountNumbers` (masked).
- **Account numbers match normalized**: letters and digits only, upper-cased (`Account.NormalizeAccountNumber`), so
  a hand-created "1234 5678" matches a statement's "1234-5678".
- **Statements for the same account in one file** are imported together (one holding resolver, one snapshot set),
  so they can't create a holding or snapshot twice.

### Adding a provider parser

1. Implement `IPortfolioFileParser` (populate the shared `ParsedTransaction`/`ParsedPosition` records —
   no new parsed models needed). Mark settlement-fund rows/positions with `IsSettlementFund`, and report anything you
   can't map in `ParsedPortfolioFile.Warnings` (an `ImportWarningCollector` groups them) — never drop a row silently.
   A row that can't be read is a `RowFailed` warning, not an exception that fails the file.
2. Register it in `DependencyInjection.AddApplication`.
3. Set `Priority` above any generic parser and give it distinctive `CanParseAsync` signature detection.

That's the whole extension surface — the endpoints, discovery, dedup, and UI dropdown pick it up
automatically.

## QFX ingestion nuances

`QfxFileParser` (`backend/src/Vizfolio.Application/PortfolioImports/Parsers/QfxFileParser.cs`) reads OFX 1.x SGML
(leaf tags closed or not — Vanguard closes aggregates but not leaves) and OFX 2.x XML. Every investment aggregate
becomes a ledger row or a warning; one unreadable row (e.g. a bad `DTTRADE`) is a `RowFailed` warning, never a file
failure. `SUBACCTSEC`/`SUBACCTFUND` are kept as `AccountTransaction.SubAccount` (metadata only).

### QFX mapping

| OFX aggregate | `TransactionType` | Notes |
|---|---|---|
| `BUYSTOCK` `BUYMF` `BUYOTHER` `BUYDEBT` | `Buy` | fees = `COMMISSION` + `FEES` + `LOAD`; bonds labelled `BUYDEBT` in `SourceType` |
| `SELLSTOCK` `SELLMF` `SELLOTHER` `SELLDEBT` | `Sell` | `SELLSHORT`/`BUYTOCOVER` kept in `SourceType`; signs move the position |
| `BUYOPT` / `SELLOPT` | `Buy` / `Sell` | `OptionActivity` warning: options can't be priced |
| `CLOSUREOPT` | `Other`, no quantity | `OptionActivity` warning (exercise/assign/expire aren't signed consistently) |
| `INCOME` `DIV` / `INTEREST` / `CGLONG`·`CGSHORT` | `Dividend` / `Interest` / `CapitalGain` | stored **gross** (`TOTAL`); `WITHHOLDING`+`TAXES` > 0 adds a `Fee` row `{FITID}:wh`, `SourceType` "Tax withheld" |
| `INCOME` other (`MISC`) | `Other` | `UnmappedLabel` warning |
| `REINVEST` | `Reinvest` | |
| `TRANSFER` | `Transfer`, `Amount = 0` | see below |
| `RETOFCAP` | `ReturnOfCapital` | cash in, not income, not a contribution |
| `SPLIT` | `Split` | `NUMERATOR`/`DENOMINATOR` → `SplitNumerator`/`SplitDenominator`; quantity = `NEWUNITS − OLDUNITS`; amount = `FRACCASH`; no ratio → `SplitWithoutRatio` warning |
| `JRNLSEC` / `JRNLFUND` | `Journal` | neutral; `SubAccount` = `FROM→TO` |
| `MARGININTEREST` | `Interest`, negative | |
| `INVEXPENSE` | `Fee`, negative | |
| `INVBANKTRAN` | by `TRNTYPE` | see below |
| anything else | — | `UnknownAggregate` warning with count and sample `FITID`s |

Security ids: `TICKER` is the ticker; a `CUSIP` (or untyped 9-character id) keeps its CUSIP and takes the `SECLIST`
ticker; a US/CA **ISIN** yields its embedded CUSIP plus the `SECLIST` ticker; any other id type is never mistaken
for a ticker (`UnsupportedSecurityId` warning when nothing resolves).

Splits in valuation (roadmap Appendix A.7): a provider `CorporateAction` is authoritative and a broker split row
within 10 days of it adds nothing; a broker split the provider doesn't know **applies its own ratio** from its date,
or its change in shares when it has no ratio (reported as an `UnmatchedSplit` finding either way).

### Cash contributions live in `<INVBANKTRAN>` inside `<INVSTMTRS>`

OFX 2.x brokerage exports wrap cash movements (ACH deposits, withdrawals, cash sweeps) in `<INVBANKTRAN>` **inside** `<INVSTMTRS>/<INVTRANLIST>`, alongside `<BUYSTOCK>`, `<SELLSTOCK>`, `<INCOME>`, `<REINVEST>`, `<TRANSFER>`. The parser recognizes `INVBANKTRAN`, unwraps its child `<STMTTRN>`, and reuses the same TRNTYPE-to-`TransactionType` mapping used for bank statements; `<SUBACCTFUND>` is kept as the row's sub-account.

Without this handling, brokerage-account deposits and withdrawals would be silently dropped and `Contributions` would sum to `0` even when the QFX contained obvious cash movements — the exact bug that occurred prior to 2026-06-30.

### `<TRANSFER>` inside `<INVTRANLIST>` stores `Amount = 0`

`InvTransfer` captures `UNITS` signed by **`TFERACTION`** (`OUT` → negative, `IN` → positive, whatever the sign of
`UNITS`) and the broker's `UNITPRICE` in `Price`, with `Amount = 0` — an in-kind transfer (e.g. an ACAT) has no cash
amount in the QFX. Performance values it as a contribution at the day's close × quantity (see Contributions), so
moving an account to a new broker doesn't read as investment gain. (`AVGCOSTBASIS` is cost, not value, and isn't used.)

### Positions

Each `INVPOSLIST` position becomes a `BrokerPosition` snapshot at the statement's `DTASOF`, with its
`DTPRICEASOF` kept as `AccountHoldingSnapshot.PriceAsOf`. A `POSTYPE` `SHORT` position is stored negative.

### `<INVBAL>` available cash anchors the account's cash

`AVAILCASH` is read into `ParsedAccountStatement.Cash` (with `MARGINBALANCE`/`SHORTBALANCE`; a non-zero one adds a
`MarginBalance` warning — only available cash is valued). When it isn't already the settlement fund's position, the
import records it as a `BrokerPosition` snapshot of the account's `$CASH` holding, which anchors the cash roll like
the settlement fund's snapshots do. When it **is** the settlement fund (`Cash.IncludesSettlementFund`), the fund's
position anchors cash and `AVAILCASH` is not recorded, so cash is never counted twice.

### Broker profiles

Per-broker OFX conventions live in `IBrokerProfile` implementations (`.../PortfolioImports/Brokers/`), matched by
`BROKERID` and registered in `DependencyInjection.AddApplication`; `BrokerProfiles.For` falls back to
`DefaultBrokerProfile`. A profile holds **no ticker lists** — it describes how the broker *labels* things:

| Profile | Sweep rows | `AVAILCASH` |
|---|---|---|
| Vanguard (`vanguard.com`) | `MEMO` "MONEY FUND PURCHASE" / "MONEY FUND REDEMPTION" → `IsSettlementFund` | **is** the settlement fund's position (verified against real exports: it equals the settlement fund's `MKTVAL`), so that position is marked and `AVAILCASH` isn't recorded |
| Default (any other broker) | none | the position whose `MKTVAL` equals `AVAILCASH` (exactly one, non-zero) is the settlement fund; otherwise `AVAILCASH` is cash of its own |

Positions whose ticker a sweep row moves are marked as the settlement fund too. To add a broker (Fidelity core
position, Schwab bank sweep), implement `IBrokerProfile` from a real export and register it.

## Reconciliation (future work)

Snapshots and transactions describe the same account from two angles, and it's tempting to want the service to *reject* a snapshot that doesn't tie out against the ledger. In practice we deliberately don't, because two invariants behave differently:

- **Quantity is a hard invariant.** For any holding, `expectedQtyAtB = qtyAtA + Σ (Buy.qty − Sell.qty + Reinvest.qty + Transfer.qty) over (A, B]`. When `snapshot.Quantity` disagrees with `expectedQty`, the ledger is genuinely wrong — a transaction is missing, duplicated, or a corporate action (split, spin-off) wasn't captured. This is actionable, and the fix belongs in the ledger.
- **Market value is a soft invariant.** `actualValueDelta − Σ contributions − Σ income` equals *implied market movement* (unrealized gain/loss + reinvested dividends at unknown prices + FX + fees not otherwise captured). That number is never zero for a snapshot pair spanning real time, because market movement is by definition not in the transaction ledger. There is no threshold at which "tied out" is a defensible cutoff.

Rejecting a snapshot on the strict value delta would therefore reject *every honest snapshot*. Rejecting on quantity mismatch would block partial-history users (whose earlier transactions aren't imported yet) from ever recording an opening balance.

The right posture is **accept always, report separately**. A future `GET /api/portfolios/{id}/accounts/{accountId}/reconciliation` endpoint would walk adjacent snapshot pairs and, for each holding, return findings labeled:

- `QuantityMismatch` — hard, actionable. `expectedQty ≠ snapshot.Quantity`.
- `LargeValueDelta` — soft, informational. `|impliedMarketMovement|` exceeds a configurable ratio of the prior balance (default: 3× — twenty-five hundred percent between two snapshots is a real signal, five percent is not).

The endpoint would be report-only; imports and `POST .../opening-balance` continue to succeed regardless. The UI decides whether to render findings quietly on a data-hygiene screen or loudly on onboarding, and could later persist per-finding acknowledgements.

**Partly built.** The account engine now performs this reconciliation while valuing: at every statement it compares
the rolled quantity (and cash) with the broker's, records a `QuantityMismatch` / `CashMismatch` finding with its
value, and — only when the drift is *material* (see Completeness) — treats the component as unknown back to the
previous statement. Snapshots are still always accepted. Not built yet: the report endpoint and UI (roadmap Phase
6.1, "Data Health"), which will surface `AccountStateEngine.Findings`.

## Extending the response

New sibling metrics slot onto `PerformanceReturnsResult` or as top-level fields on `PortfolioPerformanceResult`. Route and existing metrics don't change. The mapping layer (`backend/src/Vizfolio.Api/Endpoints/Portfolios/PerformanceMapping.cs`) projects Application-layer records into API records; add a new mapping method there when the shape grows.

`AccountValuationLoader` loads each account's whole history once per request (holdings, snapshots, ledger, prices, splits — a handful of queries) and every balance, flow and series point is computed in memory by the engines. Adding a metric that needs the same data should use the loaded engines rather than requerying.

## Holdings & ledger endpoints

Two account-scoped read endpoints back the **Holdings** and **Ledger** tabs of the account detail UI. They live alongside the performance endpoints and share its conventions (FastEndpoints, `AllowAnonymous`, account-in-portfolio scope → **404**).

| Route | Verb | Handler |
|---|---|---|
| `/api/portfolios/{portfolioId}/accounts/{accountId}/holdings` | GET | `GetAccountHoldingsEndpoint` |
| `/api/portfolios/{portfolioId}/accounts/{accountId}/ledger` | GET | `GetAccountLedgerEndpoint` |
| `/api/portfolios/{portfolioId}/accounts/{accountId}/opening-positions` | GET | `GetOpeningPositionsEndpoint` |

**Holdings** (`HoldingResponse[]`, ordered by symbol) lists each `AccountHolding` valued on `asOf` by **exactly the performance balance rules** (the account's `AccountStateEngine`), so the Holdings and Performance views always agree. Query param `asOf` (ISO date) defaults to today (UTC). The account's **cash** — uninvested cash plus its settlement fund — is **one row**, `kind: "Cash"`, listed last (`valuationSource: "Cash"`); the settlement fund isn't listed separately.

- `status`: `Valued`, `NotHeld` (a true $0 on that date; `quantity` and `marketValue` are 0) or `Missing` (held but no recent price or snapshot — `marketValue` is `null`, `quantity` is the ledger's).
- `valuationSource` (`Price` | `Snapshot` | `StableNav` | `Cash`) and `priceAsOf` (date of that price or snapshot) say how a value was derived; `unitPrice` is the per-share price used.
- `hasSnapshot` / `snapshotAsOf` / `source` describe the latest snapshot on or before `asOf`, whether or not it was used.
- `costBasis` comes from that snapshot and is reported only while the position still matches it (same quantity); `gainLoss` is `marketValue − costBasis` when both are present.

**Opening positions** (`OpeningPositionsResponse`) are the account's starting positions — what it held the day before its first transaction (`asOf`) — derived from the broker's statements. Each holding (and `cash`) has `quantity`, `class` (`None` = nothing held, `PreHistory` = held before the imported history, `Inconsistent` = the ledger and the broker disagree so it can't be derived), `verified` (false when no statement exists to derive it from, i.e. assumed zero) and `unitPrice` / `marketValue` at `asOf`. The opening-balance form prefills from it.

**Ledger** (`LedgerEntryResponse[]`, newest trade date first) lists the account's `AccountTransaction`s. Optional `from`/`to` filter on `TradeDate` (**400** on `from > to`, matching performance); omit for full history. `type` is the normalized `TransactionType`; `sourceType` preserves the broker's original label; `holdingName` carries the linked holding's name when the transaction is linked. The newest-first sort is applied in memory (SQLite can't `ORDER BY` the `DateTimeOffset` tiebreak — keep it provider-agnostic).
