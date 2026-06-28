# Fund Source

This is a repository containing open, snapshotted fund information from Edgar.

A manifest containing all the supported funds there is data for is located at https://github.com/vizfolio/fund-extracts/blob/master/funds.json. Its schema definition is located under https://github.com/vizfolio/fund-extracts/blob/master/schemas/funds_manifest.json.

The full snapsot of a given fund are in a gzip archive located under snapshots/{series_id}/{latest_period}.json.gz. The schema for this file is under https://github.com/vizfolio/fund-extracts/blob/master/schemas/fund_snapshot.json.
