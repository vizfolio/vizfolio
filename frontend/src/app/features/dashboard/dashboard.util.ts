import { PortfolioPerformance } from '../../core/api/models/performance.models';

// Shared performance formatters live in shared/util so every performance view (dashboard,
// account tab, portfolio Performance page) uses one implementation. Re-exported here to keep
// existing dashboard imports stable.
export {
  formatCurrency,
  formatPercent,
  trendOf,
  buildSyntheticSeries,
} from '../../shared/util/performance-format';
export type { SyntheticSeries } from '../../shared/util/performance-format';

/** Sample data used to keep the dashboard legible when the DB has no portfolios yet. */
export const SAMPLE_PERFORMANCE: PortfolioPerformance = {
  from: '2025-07-01',
  to: '2026-07-01',
  startingBalance: {
    value: 42000,
    isComplete: true,
    snapshotAsOf: '2025-07-01',
    holdingsCovered: 3,
    holdingsMissingSnapshot: 0,
  },
  endingBalance: {
    value: 58750,
    isComplete: true,
    snapshotAsOf: '2026-07-01',
    holdingsCovered: 3,
    holdingsMissingSnapshot: 0,
  },
  contributions: {
    net: 9000,
    deposits: 12000,
    withdrawals: -3000,
    count: 14,
  },
  returns: {
    timeWeighted: {
      rate: 0.114,
      method: 'ModifiedDietz',
      basis: 'Period',
      reason: null,
    },
    moneyWeighted: {
      rate: 0.121,
      method: 'XIRR',
      basis: 'Annualized',
      reason: null,
    },
  },
  currencyCode: 'USD',
};
