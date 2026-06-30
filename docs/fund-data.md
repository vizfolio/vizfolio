# Fund Source

This is a repository containing open, snapshotted fund information from Edgar.

A manifest containing all the supported funds there is data for is located at https://raw.githubusercontent.com/vizfolio/fund-extracts/refs/heads/master/funds.json. Its schema definition is located under https://raw.githubusercontent.com/vizfolio/fund-extracts/refs/heads/master/schemas/funds_manifest.json.

The full snapsot of a given fund are in a gzip archive located under snapshots/{series_id}/{latest_period}.json.gz. The schema for this file is under https://raw.githubusercontent.com/vizfolio/fund-extracts/refs/heads/master/schemas/fund_snapshot.json.

There is also support for Collective Investment Trusts: https://raw.githubusercontent.com/vizfolio/edgar-extract/refs/heads/master/config/cit_substitutions.json. In these cases, we will map substitutions to the analagous public fund, but the data model should support importing these and they should be part of the data model.