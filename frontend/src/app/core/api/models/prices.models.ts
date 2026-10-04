/**
 * TypeScript mirrors of the price endpoints. Source of truth:
 *   backend/src/Vizfolio.Api/Endpoints/Prices/PriceStatusEndpoints.cs
 *   backend/src/Vizfolio.Api/Endpoints/Settings/PriceProviderEndpoints.cs
 *   backend/src/Vizfolio.Application/Pricing/Abstractions/IPriceRefreshQueue.cs (PriceRefreshState)
 */

/** The background price refresh: running or queued now, and how the last run went. */
export interface PriceRefreshState {
  running: boolean;
  pending: boolean;
  lastStartedAt: string | null;
  lastFinishedAt: string | null;
  lastTrigger: 'Import' | 'Schedule' | 'Manual' | null;
  lastUpserted: number | null;
  lastFailed: number | null;
  lastError: string | null;
}

export type PriceFetchOutcome = 'Ok' | 'Empty' | 'Failed' | 'NoSource' | 'AdjustedOnly';

/** One price series and how fetching it last went. */
export interface PriceSeriesStatus {
  symbol: string;
  kind: string;
  lastAttemptAt: string | null;
  lastSource: string | null;
  lastOutcome: PriceFetchOutcome;
  message: string | null;
  neededFrom: string | null;
  firstStored: string | null;
  lastStored: string | null;
  noDataBefore: string | null;
}

/** GET /api/prices/status. */
export interface PriceStatus {
  refresh: PriceRefreshState;
  /** Providers ready to fetch as-traded prices. */
  providersAvailable: number;
  /** Problems first. */
  series: PriceSeriesStatus[];
}

/** GET /api/settings/price-providers — in the order they're tried. Keys are never returned. */
export interface PriceProvider {
  provider: string;
  displayName: string;
  priority: number;
  requiresApiKey: boolean;
  available: boolean;
  /** False for providers that only have adjusted closes (Stooq), which can't value holdings. */
  providesRawCloses: boolean;
  keySource: 'None' | 'Configuration' | 'Settings';
}
