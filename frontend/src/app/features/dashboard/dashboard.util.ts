import { PortfolioPerformance } from '../../core/api/models/performance.models';

// Shared performance formatters live in shared/util so every performance view (dashboard,
// account tab, portfolio Performance page) uses one implementation. Re-exported here to keep
// existing dashboard imports stable.
export {
  formatCurrency,
  formatPercent,
  trendOf,
  buildValueSeries,
  buildReturnSeries,
} from '../../shared/util/performance-format';
export type { ReturnSeries, ValueSeries } from '../../shared/util/performance-format';

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
      method: 'DailyValuedTWR',
      basis: 'Period',
      reason: null,
      annualizedRate: null,
      fallbackReason: null,
    },
    moneyWeighted: {
      rate: 0.121,
      method: 'XIRR',
      basis: 'Annualized',
      reason: null,
      annualizedRate: null,
      fallbackReason: null,
    },
  },
  currencyCode: 'USD',
  series: {
    interval: 'Monthly',
    points: [
      { date: '2025-06-30', value: 42000, deposits: 0, withdrawals: 0, cumulativeReturn: 0, investmentGain: 0 },
      { date: '2025-07-31', value: 43100, deposits: 1000, withdrawals: 0, cumulativeReturn: 0.0016, investmentGain: 100 },
      { date: '2025-08-31', value: 43900, deposits: 1000, withdrawals: 0, cumulativeReturn: -0.0016, investmentGain: -100 },
      { date: '2025-09-30', value: 44200, deposits: 1000, withdrawals: -1500, cumulativeReturn: 0.0112, investmentGain: 700 },
      { date: '2025-10-31', value: 46000, deposits: 1000, withdrawals: 0, cumulativeReturn: 0.0237, investmentGain: 1500 },
      { date: '2025-11-30', value: 47800, deposits: 1000, withdrawals: 0, cumulativeReturn: 0.036, investmentGain: 2300 },
      { date: '2025-12-31', value: 49100, deposits: 1000, withdrawals: 0, cumulativeReturn: 0.0402, investmentGain: 2600 },
      { date: '2026-01-31', value: 50300, deposits: 1000, withdrawals: 0, cumulativeReturn: 0.0428, investmentGain: 2800 },
      { date: '2026-02-28', value: 51200, deposits: 1000, withdrawals: -1500, cumulativeReturn: 0.0646, investmentGain: 4200 },
      { date: '2026-03-31', value: 53400, deposits: 1000, withdrawals: 0, cumulativeReturn: 0.0821, investmentGain: 5400 },
      { date: '2026-04-30', value: 55100, deposits: 1000, withdrawals: 0, cumulativeReturn: 0.0917, investmentGain: 6100 },
      { date: '2026-05-31', value: 56900, deposits: 1000, withdrawals: 0, cumulativeReturn: 0.1026, investmentGain: 6900 },
      { date: '2026-06-30', value: 57800, deposits: 1000, withdrawals: 0, cumulativeReturn: 0.1, investmentGain: 6800 },
      { date: '2026-07-01', value: 58750, deposits: 0, withdrawals: 0, cumulativeReturn: 0.114, investmentGain: 7750 },
    ],
  },
};
