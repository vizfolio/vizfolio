# PriceHistory valuation — windowed-return bug & the shipped fix

> **STATUS: IMPLEMENTED.** The `PriceHistory` table + fetch pipeline and the resolved valuation
> described below have shipped. This document is kept as the design rationale — read it before
> touching `backend/src/Vizfolio.Application/Portfolios/` valuation code or the price pipeline.
>
> **What shipped:**
> - Domain: `PriceHistory` + `CorporateAction` (`backend/src/Vizfolio.Domain/Pricing/`), raw/as-traded basis.
> - Roll-forward + resolver: `HoldingQuantityCalculator`, `HoldingValuationResolver`, wired into
>   `PortfolioPerformanceService.BuildResolverAsync` / `ComputeBalance`.
> - Pluggable fetch: `IPriceHistorySource` (`TiingoPriceHistorySource` recommended, `AlphaVantagePriceHistorySource`,
>   `EodhdPriceHistorySource`; `StooqPriceHistorySource` off by default — adjusted closes), tried as a fallback chain by
>   `PriceHistoryImporter`; fetched automatically in the background (`PriceRefreshQueue`/`PriceRefreshWorker`) after
>   imports, at startup and daily. See "Price providers" and "Fetching prices automatically" below.
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

0. **Fresh broker snapshot** — a valued snapshot with no share-affecting trade after it and no
   price newer than it is used as-is (it *is* the latest truth, and keeps Performance in step
   with the Holdings view). See §9.
1. **PriceHistory** — `quantity(snapshot-anchored ledger) × price(from PriceHistory)`. Preferred
   whenever a price exists for that security on/before that date and the quantity is positive.
2. **Broker snapshot `MarketValue`** — the broker's ground truth; keep as fallback and for
   reconciliation. If shares changed since the snapshot, the current quantity is revalued at the
   snapshot's per-share price (so a position sold to zero is `$0`, not its stale value — §9).
3. **Incomplete** — no price and no usable snapshot → the holding is *missing* and the balance
   is `IsComplete = false`, so the calculators return `null` with a reason. **Never invent a
   value.**

Everything downstream is untouched: `PerformanceComputationContext`, the return math, and the
`StartingIsComplete` / `EndingIsComplete` / `Reason` plumbing all stay exactly as they are — they
already behave correctly once fed an honest balance.

**Quantity source.** Ledger roll-forward **anchored on the latest broker snapshot** at/before the
date: `snapshot.Quantity + Σ share-affecting rows in (snapshot.AsOf, date]` — the reconciliation
invariant. With no snapshot yet, the roll-forward starts from zero at inception. See §9.

**Schema as shipped** (`backend/src/Vizfolio.Domain/Pricing/`):

- A shared price series keyed by **`Security`** (`Kind=Security`, `SecurityId`) or a bare uppercased
  **`SymbolKey`** (`Kind=Symbol`) for holdings with no SEC match — **not** per-account `AccountHolding`.
- `PriceHistory`: `Kind`, `SecurityId?`/`SymbolKey?`, `AsOf`, `Close` (raw), `CurrencyCode`, `Source`,
  `Adjusted` (always `false` — documents the raw basis).
- `CorporateAction` (separate, sparse): `Kind`, series key, `Type` (`Split`), `ExDate`,
  `SplitNumerator`/`SplitDenominator`, `Source`.
- No filtered/partial unique index (not provider-portable); one row per series per date is enforced in
  `PriceHistoryImporter`'s in-memory upsert. EF Core, provider-agnostic (per `CLAUDE.md` rule 5).

### Price providers

Providers form a **fallback chain** (`PriceHistorySourceSelector.SelectAll`): each range is asked of every provider
that supports it, highest priority first, until one returns raw closes; a provider that fails (error, rate limit) or
has no data passes to the next. API-key sources are disabled until their key is set — in Settings → Prices (saved
server-side in `AppSetting`) or in configuration, which always wins (`IProviderKeyStore`).

| Provider | Priority | Key | Basis | Splits | Mutual funds | Notes |
|---|---|---|---|---|---|---|
| **Tiingo** (recommended) | 30 | free sign-up | raw `close` (fund `close` = NAV) | `splitFactor` on the same rows | yes | Free tier: 50 req/hour, 1,000/day, 500 symbols/month, 30+ yrs history; "internal use only" |
| Alpha Vantage | 20 | yes | raw (`TIME_SERIES_DAILY`) | — | check provider | Free tier is small; check its current limits before backfilling years |
| EODHD | 10 | yes | raw `close` | separate splits call | check provider | Free tier is limited; check its current history/call limits |
| Stooq | 0 | none | ⚠️ split/dividend **adjusted** | — | — | **Off by default.** Rows stored `Adjusted = true` and never used to value anything; often gated by an anti-bot page |

**Tiingo** (`TiingoPriceHistorySource`) makes one request per symbol for the whole range
(`GET {BaseUrl}/tiingo/daily/{ticker}/prices?startDate=&endDate=`), sends the key in the
`Authorization: Token …` header (never the URL), maps a `splitFactor ≠ 1` row to a split on that day,
writes share-class tickers with a hyphen (`BRK.B` → `BRK-B`), and treats a 404 (unknown ticker) as an
empty series. Its limiter is **hourly** (`PriceHistory:Providers:Tiingo:RequestsPerHour`, default 50),
so an import with more symbols than that waits for the next hour rather than failing.

Set the key in **Settings → Prices** (stored server-side, never returned by the API), or outside the repo, e.g. an
environment variable `PriceHistory__Providers__Tiingo__ApiKey=<key>` or dotnet user-secrets (don't commit it to
`appsettings*.json`); a configured key wins over one saved in Settings. Stored prices
are never overwritten (the importer only fills missing dates), so switching providers doesn't replace
rows already fetched — clear the `PriceHistory` / `CorporateAction` rows for a series (or the dev DB)
to re-fetch it from the new source. Licensing: data is fetched per user into their own local DB for
personal use and is never redistributed via the repo — see §"Model" above.

### Fetching prices automatically

Nobody has to fetch prices by hand (roadmap Phase 3):

- **After an import** (and a reprocess that added rows), `PortfolioImportService` enqueues a refresh for the accounts it
  touched (`IPriceRefreshQueue`; switch off with `PriceHistory:RefreshOnImport`).
- **At startup** (`Schedule:RunOnStartup`, default on, after `StartupDelay`) and **daily after the US close**
  (`Schedule:DailyAt` 20:00 in `Schedule:TimeZone` America/New_York, once fund NAVs are out; `Interval` when `DailyAt`
  is unset). `appsettings.Development.json` disables the schedule; import-triggered and manual fetches still run.
- **From Settings** ("Fetch prices now", `POST /api/prices/refresh`) and whenever a provider key is saved.

One `PriceRefreshWorker` runs whatever is queued as a single run, and **waits** for the shared `ImportRunGate`
(never overlapping an extracts import, never skipped). While an account's refresh is queued or running, a value
missing for want of a price reports `PricesPending` instead of `NoPrice`/`StalePrice`, and the UI shows "Updating…"
and polls until it clears.

**What's fetched.** One series per security/symbol held (never the `$CASH` holding), needed from a week before the
first account holding it starts trading. Coverage counts **raw** closes only, so adjusted rows never block a raw
backfill. Missing ranges are the gap before the earliest stored close and the tail from the latest (refetched in case
the provider corrected it). When every provider answers a backfill with nothing, the series records `NoDataBefore` and
that range isn't asked for again — e.g. money market funds whose provider history starts years after they were held
(valued at their stable price before that).

**Outcomes.** Each series' last attempt is kept in `PriceSeriesStatus`: `Ok` (raw closes stored; a message notes
history starting after `NeededFrom`), `Empty` (no data from any provider), `Failed` (every provider errored),
`NoSource` (no provider set up) or `AdjustedOnly`. `GET /api/prices/status` lists them, problems first, with the
refresh state; non-`Ok` series are also the run's `ImportResult.Failures`.

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
     (Tiingo `close`, not `adjClose`; EODHD `close`, not `adjusted_close`; Alpha Vantage
     `TIME_SERIES_DAILY`). ⚠️ The keyless Stooq
     daily feed is split/dividend *adjusted*, so it's **off by default** and its rows are stored with
     `Adjusted = true`, which valuation never reads (a migration marked any existing Stooq rows). ⚠️ Stooq also gates automated requests with a
     200-OK HTML JavaScript proof-of-work challenge (common from server/datacenter IPs);
     `StooqPriceHistorySource` detects that page and throws a clear error instead of importing zero
     rows, and sends a browser-like `User-Agent` (reduces but doesn't eliminate it). Use an API-key
     provider for dependable fetching.
  2. **`Split` adjusts quantity, `CorporateAction`-authoritative.** `HoldingQuantityCalculator`
     multiplies the running quantity by the split factor **only** when a matching `CorporateAction`
     exists for the `Split` row's date; otherwise it's a logged no-op (never silently corrupts).
     This also avoids double-counting when a broker already reports post-split `UNITS` on later trades.
- **Quantity sign & income rows in the roll-forward.** Brokers disagree on the sign of sold units
  (OFX `UNITS` and the Vanguard report are *negative* for sells), so a `Sell` always subtracts
  `|quantity|`. `Buy`/`Reinvest`/`Transfer` add their signed quantity. A `Dividend`/`CapitalGain`
  row that **carries a quantity** is a reinvestment recorded on the income row itself (older
  Vanguard reports, pre-2018) and adds those shares; a cash distribution (null quantity) has no
  share effect.
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
3. `ComputeBalance` consumes the resolver: fresh snapshot → `MarketValue` (§9); `NotHeld` →
   $0-complete (§7); held+price → valued; held+snapshot → `MarketValue` (revalued at its per-share
   price if shares changed since, §9); held+neither → incomplete (honest null).
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
and the keyless Stooq feed is split/dividend-adjusted and bot-gated (see §5), so a raw series + split
events want an API-key provider (Tiingo recommended).

## 9. Ledger drift vs. the broker snapshot (FIXED)

**Symptom.** An account's **Holdings** tab (snapshot-valued) was correct, but its **Performance**
tab showed a wrong — even negative — ending balance, nonsensical contributions and a wild TWR.
Typical trigger: a closed-out account (e.g. an IRA whose money was converted elsewhere) whose Holdings are all `$0`.

**Root causes (all in the quantity roll-forward that feeds §4 step 1):**

1. **Sell sign.** `Sell` did `quantity -= Quantity`, but importers store sold units as negative, so
   every sell *added* shares.
2. **Roll-forward ignored the broker.** Quantity was rolled from zero over the whole ledger, so any
   gap in imported history (e.g. unrecognised reinvestments) drifted permanently — even negative —
   and `quantity × price` then **overrode** a broker snapshot saying 0 shares / `$0`.
3. **Stale snapshot fallback.** With no price, the latest snapshot's `MarketValue` was used even
   when later trades had changed (or zeroed) the position.
4. **Unmapped Vanguard labels** landed as `Other` and so moved neither shares nor contributions
   (see [performance-api.md → Vanguard mapping](./performance-api.md#import-pipeline--parser-plugins)).
5. **Reinvested income rows** (a `Dividend` carrying shares) were ignored by the roll-forward.

**Fix (in `HoldingQuantityCalculator` / `HoldingValuationResolver`).**

- A sell always reduces the position; income rows with a quantity add shares (§5).
- **Snapshot anchoring:** `quantity(date) = snapshot.Quantity + Σ rows in (snapshot.AsOf, date]`
  using the latest snapshot at/before the date (`RollForward`). A snapshot is an **end-of-day**
  position, so rows *on* its date are already included. A value-only snapshot (quantity 0 but a
  positive market value) carries no usable quantity, so the full ledger is rolled from zero.
- **Fresh snapshot wins:** no share activity since it and no newer price → its `MarketValue` as-is.
- **Negative quantity is not priced** — it signals an inconsistent ledger and falls through to
  the snapshot.
- **Stale snapshot revaluation:** without a price, if shares changed since the snapshot, value
  `quantity × (snapshot.MarketValue / snapshot.Quantity)`; a position at 0 shares is `NotHeld` (`$0`).

**Opening balance semantics.** Because snapshots are end-of-day, an opening balance dated the day
*before* the first transaction (the UI's suggested date) should hold the pre-activity positions
(usually 0). If the first transaction is a purchase with **no funding row** in the import (common in
old mutual-fund-only reports), the money appears from nowhere; instead date the opening balance on
the purchase date and enter the purchased position — the anchor then already includes the buy.
**Superseded:** unfunded purchases are now recorded as implied contributions at import (see
[performance-api.md → Implied contributions](./performance-api.md#implied-contributions)), so keep
the suggested day-before `$0` opening balance; entering the purchase as an opening balance would
count it twice.

**Tests.** `PortfolioPerformanceServiceTests` (ledger missing early history vs. a `$0` snapshot;
anchored roll-forward; snapshot newer than the latest price; unpriced position sold to zero;
revaluation at the snapshot's unit price) and `HoldingQuantityCalculatorTests` (sell sign, reinvested
income rows, anchored roll-forward, cash-only income isn't share activity).

## 10. Silent "complete" gaps (FIXED, 2026-10)

Found by checking real data against broker statements (roadmap `.claude/plans/accuracy-and-import-ux-roadmap.md`,
Phase 1). Each produced a wrong number **reported as complete**:

- **A "nothing held" snapshot valued a held position at $0.** A `$0` / 0-share opening balance at inception was the
  latest valued snapshot for years; with no price yet (e.g. a money-market fund whose provider history starts
  later), fallback (2) returned its `$0` as *covered*. Such a snapshot carries no price, so it's now skipped for a
  position the ledger says is held → `Missing` (honest null).
- **Money-market funds before their price history.** `StablePrices` (the SEC N-MFP registry, or ≥ 20 unchanging closes) values
  a held stable-NAV fund at `quantity × $1.00` when there's no recent close. The full price series is loaded (not
  just closes ≤ `to`) so the $1.00 test works for periods before the series starts.
- **Stale closes.** A close older than `Valuation:MaxPriceAgeDays` (default 10) is "no price" — a delisted or merged
  ticker is no longer valued at a years-old close. Stable-NAV funds are exempt. (Snapshot-derived unit prices are
  not age-limited yet; that comes with the account valuation engine, roadmap Phase 4.)
- **Holdings dropped from scope.** Every holding of the in-scope accounts is valued (see performance-api.md
  "Completeness").
- **Unrecognised tickers never valued.** Every row naming a security now has a holding (er-diagram.md
  "AccountHolding"), so e.g. a delisted money-market fund's history is no longer invisible.
- **`Other` rows that carry shares** now move them (signed), e.g. Vanguard's old "Sweep" rows that move
  money-market shares out — previously a phantom position remained.

The loading and resolver build live in `HoldingValuationLoader`, shared by performance and the Holdings endpoint.

## 11. Account valuation engine (Phase 4)

> **This supersedes §4/§8/§9's implementation.** `HoldingQuantityCalculator`, `HoldingValuationResolver` and the
> per-holding loader are retired; their rules live on in the engine. Code: `backend/src/Vizfolio.Application/
> Portfolios/Valuation/` — `AccountStateEngine`, `AccountCash`, `AccountValuationLoader`, `SettlementFunds`,
> `StablePrices`, `PriceSeries`. Tests: `tests/.../Portfolios/Valuation/AccountStateEngineTests.cs`.

One engine per account values **every holding and the account's cash** on any date. Performance (balances, flows,
series), the Holdings endpoint, implied contributions (opening-cash seed), history coverage and the
opening-positions endpoint all read it, so every screen agrees.

**Loading.** `AccountValuationLoader` loads each account's *whole* history (holdings, snapshots, ledger incl. implied
rows, raw non-adjusted prices in the reporting currency, splits) whatever period is asked about: openings are derived
by rolling a *later* statement back, which needs the rows and statements after the period.

**Shares** (every holding except settlement funds and the cash holding):
- Row effects: Buy/Reinvest/Transfer add the signed quantity; income and `Other` rows add theirs when they carry one;
  a Sell always subtracts `|quantity|`.
- **Splits**: each provider `CorporateAction` multiplies the position at the **start of its ex-date** — no ledger Split
  row needed. A broker Split row within ±10 days of one is the same event (not applied again); one with no provider
  split is reported (`UnmatchedSplit`) and applies **its own ratio** (QFX `NUMERATOR`/`DENOMINATOR`) from its date, or
  its change in shares when it has no ratio. A no-cost share row ≈ `shares × (factor − 1)` near the
  ex-date is the broker recording the split's extra shares and is skipped.
- **Anchors**: stored snapshots (broker, statement, opening balance; value-only snapshots excluded) and the **derived
  opening**. The roll resets to each anchor; the drift found there is a `QuantityMismatch` finding.
- **Derived opening** (at `OpeningDate` = the day before the account's first transaction, when no stored position is
  on/before it): the earliest statement rolled back — undo each day's rows, then that day's split. ≈0 (under 0.001
  shares or $1 at the statement's price) → `None`; positive → `PreHistory` (held before the history, valued from
  PriceHistory there); negative → `Inconsistent` (the ledger and broker disagree; roll from zero instead). No
  statement at all → assumed zero (`Verified = false`).
- **Valuation per date**: the fallback order in performance-api.md "Balance basis" (fresh statement → recent close →
  stable price → statement per-share price → missing with a cause). Before `OpeningDate`, a `PreHistory` position is
  `BeforeHistory` (unknown) unless a stored statement covers the date.

**Cash** = uninvested cash + the settlement fund. `SettlementFunds` recognises it from the account's own history — no ticker
list: a ticker the broker's parser marked as the settlement fund (broker profiles — e.g. Vanguard's "MONEY FUND
PURCHASE" sweeps, or the position that is the statement's `AVAILCASH`), a ticker on a "Sweep…" row, or a money market fund (per the SEC registry) whose buys/sells/reinvestments come
without share counts, or that appears on statements with no movements in the ledger. Folding a fund into cash only
matters when its share history can't be rolled; one with complete share counts is valued at its stable price instead,
which gives the same answer. Per-broker knowledge lives in the QFX broker profiles (performance-api.md → "Broker
profiles"). A statement's available cash that isn't the settlement fund is recorded on the account's `$CASH` holding
and anchors cash like a settlement-fund snapshot.
- `AccountCash` gives each row's effect on its **trade date** (so cash may dip negative between a trade and the
  deposit that funds it — that's correct net value). Sweeps and settlement-fund Buy/Sell/Reinvest are neutral;
  settlement-fund income and transfers move cash; an old fund-company *positive-amount Buy of the money-market fund*
  is neutral here because its implied contribution row carries the money; reinvestments are funded by their income
  (per-day add-back, sources compared rather than summed so overlapping QFX + Vanguard-report rows don't double it).
- Anchored on each date's settlement-fund (and `$CASH` holding) snapshots; drift there is a `CashMismatch` finding.
- Opening cash (A.2): derived from the earliest cash statement over *imported* rows **only when some position is
  `PreHistory`** (a partial history); otherwise $0, and unfunded purchases are implied contributions as before.

**Materiality.** A drift at a statement worth more than `max(MismatchMaterialityMin ($10), MismatchMaterialityPct
(0.5%) × account value there)` makes that component `MaterialMismatch` (missing) between the previous anchor and the
statement; a smaller one is just recorded. On real multi-year Vanguard data every share position reconciles
exactly with the statements, and the cash roll lands within a few dollars of the settlement-fund balances.

**Flows.** Deposits, withdrawals and cash transfers at their amounts; implied contributions as their own kind; an
in-kind security transfer at its reported amount, else valued at the day's close × quantity (then the row's
`Price`, then the latest statement's per-share price) — unvalued flows make both returns `null` (`UnvaluedTransfer`).

**Known limits:** the opening is the
day before the first *transaction*, not the statement's `DTSTART`; the age limit doesn't yet apply to
statement-derived prices (waits for automatic price fetching).

**Money market reference data (no ticker lists).** Which tickers are money market funds, and at what stable price,
comes from SEC Form N-MFP: the edgar-extract pipeline publishes `money_market_funds.json` (every N-MFP filer —
~330 funds, ~900 tickers — with `seeks_stable_price` / `stable_price_per_share` / category, tickers from SEC's
`company_tickers_mf.json`). `MoneyMarketFundsImporter` loads it into `MoneyMarketFund` on every reference-data refresh;
`AccountValuationLoader` sets each holding's `IsMoneyMarket` and stable price from it. Institutional prime and
tax-exempt funds float, so a money market fund is only valued at a fixed price when its filing says so.

