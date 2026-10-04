## Endpoints

| Route | Verb | Handler |
|---|---|---|
| `/api/portfolios/{portfolioId}/accounts/{accountId}/history-coverage` | GET | `GetAccountHistoryCoverageEndpoint` |
| `/api/portfolios/{portfolioId}/accounts/{accountId}/opening-balance` | POST | `SetOpeningBalanceEndpoint` |

The last two endpoints exist to close the "no historical data" gap that partial imports create — see the [History coverage and opening balance](#history-coverage-and-opening-balance) section below.

## History coverage and opening balance

When a user imports a single QFX with only the past year of activity, the only snapshot is at that file's `<DTASOF>` (the end of the imported period). Any performance query whose `from` predates that snapshot will show `startingBalance.isComplete: false` and null returns with `Reason = "IncompleteStartingBalance"`. Two endpoints let a UI detect that state and let the user close it without waiting for another statement.

### `GET /api/portfolios/{portfolioId}/accounts/{accountId}/history-coverage`

Reports whether the account has a *history gap*: its **starting positions can't be derived**. Normally they can — the
valuation engine rolls the broker's earliest statement back over the imported transactions to the day before the first
one (see [price-history-valuation.md §11](./price-history-valuation.md#11-account-valuation-engine-phase-4)), so a
partial history (e.g. an 18-month QFX) needs **no manual opening balance**. A gap remains only when that roll-back gives
a negative position (or cash): the ledger and the broker disagree, so the user has to say what was held.

```jsonc
{
  "accountId": "…",
  "firstTransactionDate": "2025-06-15",
  "earliestSnapshotDate": "2026-06-01",  // earliest snapshot with a non-null MarketValue
  "hasHistoryGap": false,                // true only when a starting position can't be derived (ledger ≠ broker)
  "suggestedOpeningDate": "2025-06-14",  // firstTransactionDate - 1, for use as the default asOf in the opening-balance form
  "openingBalanceSnapshotCount": 0,
  "statementSnapshotCount": 0,
  "brokerPositionSnapshotCount": 1
}
```

- All fields are read-only projections — no writes.
- The date fields are all nullable. An empty account (no transactions, no snapshots) returns them all as `null` and `hasHistoryGap: false`.
- The three snapshot-count fields let the UI distinguish "no coverage at all" from "coverage exists but only from broker statements" from "user has already supplied an opening balance."
- `earliestSnapshotDate` is the earliest snapshot with a market value (informational). The count fields are raw counts across all sources.
- The starting positions themselves are served by `GET .../opening-positions` (see [performance-api.md → Holdings & ledger endpoints](./performance-api.md#holdings--ledger-endpoints)).

### `POST /api/portfolios/{portfolioId}/accounts/{accountId}/opening-balance`

Records the user-attested per-holding position at a chosen date. Creates one `AccountHoldingSnapshot` per holding with `Source = OpeningBalance` and `AsOf = request.asOf`.

Request:

```jsonc
{
  "asOf": "2025-06-14",
  "defaultCurrencyCode": "USD",
  "holdings": [
    { "symbol": "VOO",  "units": 10, "marketValue": 5000, "unitPrice": 500, "costBasis": 4500 },
    { "symbol": "AAPL", "units": 5,  "marketValue":  900, "unitPrice": 180, "costBasis":  800 }
  ]
}
```

- `defaultCurrencyCode` is applied per holding when the holding omits `currencyCode`.
- `symbol` is required per holding; everything else is optional except `units`.
- **Market value is derived when omitted.** If a holding supplies `unitPrice` but no `marketValue`, the snapshot is stored with `MarketValue = units × unitPrice` (reflected back in the response's per-holding `marketValue`). A holding with neither `marketValue` nor `unitPrice` is stored with `MarketValue = null` — an *incomplete* opening balance that does not close the history gap and still reports `IncompleteStartingBalance` in performance. This is the single completeness rule shared by the history-coverage gap, the opening-balance screen, and the performance starting balance.
- **Cash:** the symbol **`$CASH`** (case-insensitive) records the account's cash on its single `Kind = Cash` holding (created on first use). Cash snapshots anchor the cash roll alongside the settlement fund's.
- If a holding's `symbol` matches an existing per-account `AccountHolding.Symbol` (case-insensitive), that holding is reused. Otherwise a new `AccountHolding` is created with `Kind = Other`, holding the symbol and (if provided) `Cusip`. A subsequent import + relinker pass can promote it to `Kind = Security` or `Kind = Fund` when reference data catches up.

Response:

```jsonc
{
  "accountId": "…",
  "asOf": "2025-06-14",
  "snapshotsCreated": 2,
  "snapshotsUpdated": 0,
  "holdings": [
    { "symbol": "VOO", "accountHoldingId": "…", "quantity": 10, "marketValue": 5000, "unitPrice": 500, "costBasis": 4500, "currencyCode": "USD", "created": true },
    { "symbol": "AAPL", "accountHoldingId": "…", "quantity":  5, "marketValue":  900, "unitPrice": 180, "costBasis":  800, "currencyCode": "USD", "created": true }
  ]
}
```

### Semantics: replace-in-place at the same `asOf`

If a snapshot already exists at `AsOf` for a resolved holding (regardless of its `Source`), it is **deleted and replaced** with the new `OpeningBalance` snapshot. `snapshotsUpdated` increments in the summary, and the per-holding `created: false` marks the row. This makes the endpoint idempotent-with-overwrite: hitting POST again with better numbers just replaces the previous entry.

Rationale: the user calling this endpoint is explicitly asserting "here's my opening balance." Silently refusing to overwrite an existing `BrokerPosition` at the same date would leave stale data in place with no signal to the user. If more nuanced conflict handling is needed later (e.g., a `force` flag or a separate "delete opening balance" endpoint), it can be added without breaking today's callers.

### Recommended UI flow

1. After an import, nothing is required: starting positions are derived from the broker's statements.
2. The opening-balance form ("adjust starting positions") prefills from `GET .../opening-positions`: positions held
   before the imported history are filled in for the user to confirm or correct, positions that can't be derived
   (`Inconsistent`) are left blank, and cash appears as a `$CASH` row. Submitting stores `OpeningBalance` snapshots,
   which always override the derived opening.
3. When `hasHistoryGap` is true, prompt the user to supply the starting position for the holdings that couldn't be
   derived (the rows left blank).

### Why "first transaction date − 1" as the default `asOf`

The `suggestedOpeningDate` returned by coverage is `firstTransactionDate - 1`. Not because the data model requires it there — an opening snapshot just has to sit at-or-before whatever `from` the user queries — but because it's a defensible UI default:

- Every transaction the user has activity for has a preceding balance to reconcile against.
- Any performance query with `from >= suggestedOpeningDate` will produce `startingBalance.isComplete: true`.
- Queries starting *before* the opening date still return `IncompleteStartingBalance` — which is honest. If the user has more history than one QFX covers, they can supply an earlier `asOf` and the API accepts it.

### Enforcement policy

Coverage is a **flag**, not an enforcement gate. The performance endpoints don't refuse to answer when `hasHistoryGap` is true; they return `IncompleteStartingBalance` with a zero starting balance and let the UI decide how prominently to warn. This keeps the app usable for users who never intend to supply historical data and don't need historical returns.