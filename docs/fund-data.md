# Fund Source

This is a repository containing open, snapshotted fund information from Edgar.

A manifest containing all the supported funds there is data for is located at https://raw.githubusercontent.com/vizfolio/fund-extracts/refs/heads/master/funds.json. Its schema definition is located under https://raw.githubusercontent.com/vizfolio/fund-extracts/refs/heads/master/schemas/funds_manifest.json.

The full snapsot of a given fund are in a gzip archive located under snapshots/{series_id}/{latest_period}.json.gz. The schema for this file is under https://raw.githubusercontent.com/vizfolio/fund-extracts/refs/heads/master/schemas/fund_snapshot.json.

There is also support for Collective Investment Trusts: https://raw.githubusercontent.com/vizfolio/edgar-extract/refs/heads/master/config/cit_substitutions.json. In these cases, we will map substitutions to the analagous public fund, but the data model should support importing these and they should be part of the data model.

## Money market fund registry

`money_market_funds.json` at the root of fund-extracts lists **every** money market fund, not just the configured ones:
series ID, name, registrant CIK, N-MFP `category` (Government, Prime, Single State, Other Tax Exempt…),
`seeks_stable_price`, `stable_price_per_share` (usually 1.0; some funds use 10 or 100), `is_retail`, the source N-MFP
filing, and `classes` (`class_id`, `ticker`). Schema: `schemas/money_market_funds.json`. It's built by the
edgar-extract pipeline (`pipeline/money_market.py`) from the latest Form N-MFP3 filings plus SEC's mutual-fund ticker
map, and is refreshed with the fund snapshots.

Vizfolio imports it into `MoneyMarketFund` (`MoneyMarketFundsImporter`, run by "import all" and the scheduled extracts
refresh) and uses it to value money market funds at their stable price and to recognise settlement funds — see
[price-history-valuation.md §11](./price-history-valuation.md#11-account-valuation-engine-phase-4). Money market fund
snapshots (from N-MFP, not N-PORT) also carry a `money_market` block with the same price facts.

