# PriceHistory valuation — windowed-return bug & the shipped fix

> **STATUS: IMPLEMENTED.** The `PriceHistory` table + fetch pipeline and the resolved valuation
> described below have shipped. This document is kept as the design rationale — read it before
> touching `backend/src/Vizfolio.Application/Portfolios/` valuation code or the price pipeline.
>
> **What shipped:**
> - Domain: `PriceHistory` + `CorporateAction` (`backend/src/Vizfolio.Domain/Pricing/`), raw/as-traded basis.
> - Roll-forward + resolver: `HoldingQuantityCalculator`, `HoldingValuationResolver`, wired into
>   `PortfolioPerformanceService.BuildResolverAsync` / `ComputeBalance`.
> - Pluggable fetch: `IPriceHistorySource` (keyless `StooqPriceHistorySource` default, optional
>   `EodhdPriceHistorySource` / `AlphaVantagePriceHistorySource`), `PriceHistoryImporter`,
>   `PriceHistoryRefreshHostedService`, and `POST /admin/imports/price-history`.
> - Model: per-user-instance fetch into the local DB for personal use — **not** a redistributed public
>   dataset (exchange price data licensing forbids that; unlike public-domain EDGAR data).
>
> Related: [performance-api.md](./performance-api.md) (balance basis, reconciliation).

## 1. Symptom & repro

Portfolio/account performance over a **since-inception** window looks reasonable. But a
window that **starts later** (last year, a few months ago) reports a Time-Weighted Return
of tens of thousands (e.g. 40,000–60,000), and the value grows the **closer the start date
gets to today**.

**Minimal repro:**

1. An account with **full transaction history** from its first trade.
2. A `$0` **opening balance** set at inception (the day before the first transaction) — this
   is *correct*: the account genuinely was worth `$0` before the first trade.
3. Only **one other snapshot**, a recent one at the far end of history (e.g. from a QFX/broker
   import at `<DTASOF>`).
4. Request performance for a mid-history window, e.g. `from = today − 1y`, `to = today`.

Observed: `startingBalance.value = 0` with `isComplete: true`, and
`returns.timeWeighted.rate` is a huge number instead of `null`. Expected: either a correct
return, or an honest "not computable."

## 2. Root cause

The `$0` opening balance is a **valued** snapshot (`MarketValue = 0`, *not* null). The
performance service values a balance at a date by taking, per holding, **"the latest snapshot
whose `AsOf ≤ date`"** — and a stale `$0` from inception wins that selection for every later
`from`.

Walk the code (`backend/src/Vizfolio.Application/Portfolios/`):

- **`PortfolioPerformanceService.cs:100-104`** — loads all snapshots with `AsOf ≤ to`. No
  filter that would exclude a stale prior snapshot from acting as a later date's valuation.
- **`PortfolioPerformanceService.cs:129-135`** — builds `relevantHoldings`, then computes
  `starting = ComputeBalance(..., from)` and `ending = ComputeBalance(..., to)`.
- **`PortfolioPerformanceService.cs:185-219` (`ComputeBalance`)** — for each holding, scans the
  descending-by-`AsOf` snapshot list and takes the first with `AsOf ≤ asOf` (`:200-203`). If
  that snapshot has a value it's counted as **covered**; `IsComplete = (missing == 0)`
  (`:206-219`). For a mid-history `from`, the only snapshot at-or-before it is the `$0`
  inception one → `StartingBalance = 0`, `missing == 0`, **`IsComplete = true`**.

Because the starting balance is reported *complete*, the completeness guard in the calculators
does **not** fire:

- **`ModifiedDietzTimeWeightedReturnCalculator.cs:14-17`** — returns `null`
  (`IncompleteStartingBalance`) only when `!ctx.StartingIsComplete`. Here it's `true`, so it
  proceeds to `:35-39`:

  ```
  denominator = StartingBalance + weightedContribution          // = 0 + small
  rate        = (EndingBalance − StartingBalance − netContribution) / denominator
              = (EMV − 0 − netContribution) / (small)           // → tens of thousands
  ```

  With `BMV = 0`, current value is divided by a tiny weighted-contribution base. The closer
  `from` is to today, the fewer in-window deposits, the smaller the denominator, the larger the
  rate. The `ZeroDenominator` guard (`:36-37`) only catches an *exact* zero, so any stray
  in-window deposit yields a huge finite number instead of `null`.

- **`XirrMoneyWeightedReturnCalculator.cs:20-31`** — same guard, and it seeds the flow stream
  with `−StartingBalance` at `from` (`:27`). A `−0` opening flow makes the IRR similarly
  meaningless.

**The `$0` is not the bug.** It is created by
`AccountHistoryService.SetOpeningBalanceAsync` (`AccountHistoryService.cs:95-165`, market value
derived from units×price at `:122-123`), and it is the *correct* market value at inception. The
bug is the performance service **reusing an inception valuation as the valuation for arbitrary
later dates**. Since-inception windows are fine precisely because `BMV = 0` is legitimately
correct there.

## 3. Financial principle

A time-weighted return (Modified Dietz) and a money-weighted return (XIRR) both require the
portfolio's **market value at each period boundary** (and, strictly, at each cash-flow date).
This is non-negotiable — it's how the return is defined.

The app can already recover **quantity** at any date from the ledger (roll forward
Buy − Sell + Reinvest + in-kind Transfer ± Split — the "quantity is a hard invariant" idea in
[performance-api.md → Reconciliation](./performance-api.md#reconciliation-future-work)). What it
*cannot* currently recover is the **price** at an arbitrary date. Snapshots only provide market
value on the sparse dates they exist. So for any `from` that isn't on (or safely near) a
snapshot date, there is no honest market value — and the current code silently substitutes a
stale one. See also the balance-basis rule in
[performance-api.md → Balance basis](./performance-api.md#balance-basis-snapshot-market-value).

## 4. The fix: PriceHistory

A `PriceHistory` table (price per security per date) supplies the missing input, so market value
can be computed for **any** date:

```
marketValue(holding, date) = quantity(holding, date) × price(security, date)
```

Sum across holdings to get BMV / EMV / interior balance points. This is standard daily-valuation
practice and makes windowed returns work reliably for all dates, not just snapshot dates.

**Integration point.** `ComputeBalance` (`PortfolioPerformanceService.cs:185`) changes from
"take the latest snapshot's `MarketValue`" to a **resolved valuation** with this fallback order,
per holding per date:

1. **PriceHistory** — `quantity(from ledger) × price(from PriceHistory)`. Preferred whenever a
   price exists for that security on/near that date.
2. **Broker snapshot `MarketValue`** — the broker's ground truth; keep as fallback and for
   reconciliation.
3. **Incomplete** — no price and no usable snapshot → the holding is *missing* and the balance
   is `IsComplete = false`, so the calculators return `null` with a reason. **Never invent a
   value.**

Everything downstream is untouched: `PerformanceComputationContext`, the return math, and the
`StartingIsComplete` / `EndingIsComplete` / `Reason` plumbing all stay exactly as they are — they
already behave correctly once fed an honest balance.

**Quantity source.** Ledger roll-forward from account inception, consistent with the
reconciliation invariant. (Snapshots also carry quantity and can cross-check it.)

**Schema as shipped** (`backend/src/Vizfolio.Domain/Pricing/`):

- A shared price series keyed by **`Security`** (`Kind=Security`, `SecurityId`) or a bare uppercased
  **`SymbolKey`** (`Kind=Symbol`) for holdings with no SEC match — **not** per-account `AccountHolding`.
- `PriceHistory`: `Kind`, `SecurityId?`/`SymbolKey?`, `AsOf`, `Close` (raw), `CurrencyCode`, `Source`,
  `Adjusted` (always `false` — documents the raw basis).
- `CorporateAction` (separate, sparse): `Kind`, series key, `Type` (`Split`), `ExDate`,
  `SplitNumerator`/`SplitDenominator`, `Source`.
- No filtered/partial unique index (not provider-portable); one row per series per date is enforced in
  `PriceHistoryImporter`'s in-memory upsert. EF Core, provider-agnostic (per `CLAUDE.md` rule 5).

## 5. Gotchas to carry forward

The expensive-to-rediscover bits — read these before implementing:

- **Split adjustment (sharpest trap).** Prices and ledger quantities must be on the **same
  basis** (both raw, or both split-adjusted). If a provider returns split-adjusted prices while
  the ledger stores as-traded quantities (or vice versa), valuations break *silently* across any
  corporate action.

  **Current basis in this codebase (verified, as of this writing):** ledger quantities are stored
  **raw / as-traded — exactly as the broker reports them** on the trade date (QFX `UNITS` in
  `QfxFileParser.cs:167/249/323`, the Vanguard `quantity` column in
  `VanguardTransactionHistoryReportParser.cs:99/133`). There is **no split-adjustment logic** —
  `TransactionType.Split` (`TransactionType.cs:15`) is a declared enum value that **nothing
  applies**: no service adjusts running quantity for a split, and the quantity roll-forward
  described in §3/§4 does not exist yet. So today a `Split` row does **not** change computed
  quantity at all.

  **Decisions taken (both explicit in the shipped code):**
  1. **Price series basis = raw (as-traded)** to match the ledger. `PriceHistory.Close` is the raw
     close; `Adjusted` is always `false`. API-key providers are queried for unadjusted closes
     (EODHD `close`, not `adjusted_close`; Alpha Vantage `TIME_SERIES_DAILY`). ⚠️ The keyless Stooq
     daily feed is split/dividend *adjusted* — it's a best-effort default; configure an API-key
     provider for a clean raw series and split events. ⚠️ Stooq also gates automated requests with a
     200-OK HTML JavaScript proof-of-work challenge (common from server/datacenter IPs);
     `StooqPriceHistorySource` detects that page and throws a clear error instead of importing zero
     rows, and sends a browser-like `User-Agent` (reduces but doesn't eliminate it). Use an API-key
     provider for dependable fetching.
  2. **`Split` adjusts quantity, `CorporateAction`-authoritative.** `HoldingQuantityCalculator`
     multiplies the running quantity by the split factor **only** when a matching `CorporateAction`
     exists for the `Split` row's date; otherwise it's a logged no-op (never silently corrupts).
     This also avoids double-counting when a broker already reports post-split `UNITS` on later trades.
- **Currency.** The price's currency must reconcile with the reporting-currency resolution the
  service already does (`ResolveReportingCurrency`, `PortfolioPerformanceService.cs:258-271`).
  Multi-currency conversion is still out of scope; don't mix currencies into one sum.
- **Coverage / honesty.** Where no price exists for a ticker/date, fall through to snapshot, then
  to `incomplete`. The completeness/honest-`null` behavior is **complementary** to PriceHistory,
  not replaced by it — PriceHistory shrinks the gap; it never eliminates the need to say "I don't
  know."
- **Interim staleness guard — no longer needed.** The stop-gap once considered here (mark a
  carried-forward snapshot incomplete when a share-changing trade falls between it and `from`) was
  never built: PriceHistory is the real fix. Where no price exists the resolver still falls through
  to the snapshot and then to an honest incomplete, so the §2 explosion only survives for a window
  with *no* price at `from` for a security — the remedy there is to import its price history.

## 7. Related item — not-yet-held holdings counted as "missing" (FIXED)

**STATUS: FIXED (shipped with §4).** This was a *separate* bug from §2 living in the same
`ComputeBalance` code and sharing the ledger roll-forward, so it landed in the same pass. Unlike §2
it needs **no price data** — only "was the holding held at `from`."

**Symptom.** The account **Holdings / History** screens and the **/dashboard** show every account's
opening balance as fully filled, but the portfolio **/performance** screen shows
`startingBalance.isComplete = false` (`holdingsMissingSnapshot > 0`).

**Concrete trigger (the reporter's setup).** A portfolio with accounts started on **different
dates** — two in **2011**, one in **2014** — each with opening balances entered for every holding.

**Why the screens disagree — two different completeness rules:**

- **Account/coverage screens are per-account, at that account's own inception.**
  `AccountHistoryService.GetCoverageAsync` (`AccountHistoryService.cs:81`) sets `hasGap` from
  "earliest *valued* snapshot ≤ that account's first transaction," and each account's opening
  balance is written at `suggestedOpeningDate = firstTx − 1` (`:82`; the form submits at exactly
  that date, `opening-balance-form.ts:99,116`). So each account is fully covered relative to *its
  own* start.
- **/performance uses one portfolio-wide `from`.** `ResolveDefaultFromAsync`
  (`PortfolioPerformanceService.cs:168-174`) resolves `from` to the **earliest transaction across
  all accounts** — here **2011**. `ComputeBalance` (`:185-219`) then marks a holding **missing** if
  it has no valued snapshot with `AsOf ≤ from`. The **2014** account's opening snapshots are dated
  ~2014, i.e. *after* the 2011 `from`, so at `from` those holdings have no snapshot yet → counted
  missing → starting balance reported incomplete. The `relevantHoldings` set
  (`:129-135`, plus `holdingsActiveInRange` `:106-114`) includes those later-account holdings, so
  they're evaluated at a date before they existed.

**Root cause.** `ComputeBalance` treats *"no snapshot at/before `from`"* as **"unknown / missing,"**
conflating it with *"not held yet, so worth $0."* A holding that wasn't held at `from` has a **true
starting value of $0** and should count as **complete**, not missing. The same bug also fires within
a single account for any holding **acquired after `from`** (bought mid-window).

**Fix as shipped (no PriceHistory needed).** In `HoldingValuationResolver`, a relevant holding that
was **not held at `from`** — ledger quantity 0 *and* no broker-position snapshot at/before `from` —
resolves to `NotHeld`: **$0 and complete** (not counted toward covered or missing). Only a holding
that *was* held at `from` (nonzero ledger position, or a snapshot reporting a position) but lacks a
valuation is `Missing`. This uses the same `HoldingQuantityCalculator` roll-forward that (b) values
held holdings via price in §4.

**Watch-outs.**
- Apply the same "held at date?" logic to the **ending balance** and interior points for
  consistency, but a fully-divested holding (held at `from`, sold before `to`) is correctly $0 at
  `to` and should stay complete.
- Keep the honest-`null` behavior for a holding that *was* held but has no valuation — that's the
  §2 case, not this one.

**Test.** Portfolio scope with **staggered account inceptions** (e.g. accounts starting 2011, 2011,
2014), opening balances on each account's own inception, default `from`: the later account's
holdings must resolve to $0-and-complete at the 2011 `from`, so `startingBalance.isComplete = true`.

## 8. What shipped (map to the code)

1. Ledger **quantity-at-date** roll-forward: `HoldingQuantityCalculator` — reused for both §4
   (valuation) and §7 (not-yet-held → $0). `Split` rows apply the authoritative `CorporateAction` factor.
2. Valuation resolver: `HoldingValuationResolver` — `quantity(ledger) × price(PriceHistory)` with the
   §4 fallback order; built by `PortfolioPerformanceService.BuildResolverAsync` (batch-loads ledger,
   prices, splits, snapshots once — no per-holding round-trips).
3. `ComputeBalance` consumes the resolver: `NotHeld` → $0-complete (§7); held+price → valued;
   held+snapshot → `MarketValue`; held+neither → incomplete (honest null).
4. Prices are filtered to the reporting currency (a null price currency, e.g. from the keyless source,
   is treated as matching); multi-currency conversion remains out of scope.
5. Tests in `backend/tests/Vizfolio.Api.Tests/`: valuation scenarios (a)–(e) in
   `Portfolios/PortfolioPerformanceServiceTests.cs`, roll-forward/split units in
   `Portfolios/HoldingQuantityCalculatorTests.cs`, and pipeline in `Pricing/` (selector + importer).
6. [performance-api.md](./performance-api.md) updated (balances valued from PriceHistory with snapshot
   fallback; not-yet-held → $0-complete). [er-diagram.md](./er-diagram.md) adds `PriceHistory` /
   `CorporateAction`.

**Remaining follow-ups (not blocking):** funds are still valued from `FundSnapshot` NAV, not
`PriceHistory` — a `Kind=Fund` holding falls through to a `Symbol` series only if it carries a ticker;
and the keyless Stooq feed is split-adjusted (see §5), so a raw series + split events want an API-key
provider.
