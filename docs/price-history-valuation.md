# PriceHistory valuation — windowed-return bug & planned fix

> **STATUS: TODO / not yet implemented.** This is a handoff note. It documents a real,
> reproducible bug in the windowed performance return, explains why the current design
> can't fix it, and specifies the `PriceHistory` design that will. The `PriceHistory`
> table and its data-fetch pipeline are being designed in a separate session; **no code
> in the performance service changes until that lands.** Read this before touching
> `backend/src/Vizfolio.Application/Portfolios/` valuation code — it exists so the fix
> can be picked up without re-deriving any of the analysis below.
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

**Schema sketch** (to be finalized in the PriceHistory design session):

- Keyed by **`Security` / `Fund`** (a shared price series), **not** per-account `AccountHolding`.
- Columns: `SecurityId` (or symbol/identifier key), `AsOf` (date), `Close` (decimal),
  `CurrencyCode`, `Source` (provider label).
- Unique on `(SecurityId, AsOf)`.
- EF Core, provider-agnostic (per `CLAUDE.md` rule 5).

## 5. Gotchas to carry forward

The expensive-to-rediscover bits — read these before implementing:

- **Split adjustment (sharpest trap).** Prices and ledger quantities must be on the **same
  basis** (both raw, or both split-adjusted). If a provider returns split-adjusted prices while
  the ledger stores as-traded quantities (or vice versa), valuations break *silently* across any
  corporate action. Decide the basis explicitly and make the roll-forward and the price series
  agree.
- **Currency.** The price's currency must reconcile with the reporting-currency resolution the
  service already does (`ResolveReportingCurrency`, `PortfolioPerformanceService.cs:258-271`).
  Multi-currency conversion is still out of scope; don't mix currencies into one sum.
- **Coverage / honesty.** Where no price exists for a ticker/date, fall through to snapshot, then
  to `incomplete`. The completeness/honest-`null` behavior is **complementary** to PriceHistory,
  not replaced by it — PriceHistory shrinks the gap; it never eliminates the need to say "I don't
  know."
- **Interim behavior (this task ships nothing here).** Until PriceHistory exists, mid-history
  windows without a real boundary valuation still report the huge number described in §2. A
  smaller stop-gap — a *staleness guard* that marks a carried-forward snapshot incomplete when a
  quantity-changing trade occurred between the snapshot and the window boundary — was considered
  and **deliberately deferred** so the real (PriceHistory) fix can be built once. If the huge
  number becomes a problem before PriceHistory lands, that guard is the minimal honest patch:
  treat the holding as missing at `from` when a share-changing transaction falls strictly between
  the snapshot's `AsOf` and `from` (exclude trades *on* `from`, which are period cash flows — the
  inception window must keep working).

## 6. Pick-up checklist (after the PriceHistory table + fetch exist)

1. Add a valuation resolver: `marketValue(holding, date)` via `quantity(ledger) × price(PriceHistory)`,
   with the fallback order in §4.
2. Swap it into `ComputeBalance` (`PortfolioPerformanceService.cs:185`); keep snapshot `MarketValue`
   as the second-tier fallback and `incomplete` as the last resort.
3. Reuse the already-loaded ledger/snapshot data where possible — the service note in
   [performance-api.md → Extending the response](./performance-api.md#extending-the-response) warns
   against extra round-trips.
4. Tests: a mid-history window (the §1 repro) now returns a **sensible** TWR/MWR from PriceHistory;
   a window over a security with **no** price still returns `null` with a completeness reason; the
   since-inception window is unchanged. Put these in
   `backend/tests/Vizfolio.Api.Tests/Portfolios/PortfolioPerformanceServiceTests.cs`.
5. Update [performance-api.md](./performance-api.md): document that balances are valued from
   PriceHistory with snapshot fallback, and retire the "partial-history starts at 0" caveat where
   PriceHistory now covers it.
6. Delete or update this file once the fix is implemented.
