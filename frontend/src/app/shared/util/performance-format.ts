import { PortfolioPerformance } from '../../core/api/models/performance.models';

/** Formats a number as currency for the given ISO code (falls back to plain if invalid). */
export function formatCurrency(value: number, currencyCode: string): string {
  try {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: currencyCode || 'USD',
      maximumFractionDigits: 0,
    }).format(value);
  } catch {
    return `${value.toFixed(0)} ${currencyCode}`;
  }
}

/** Formats a monetary value with cents (525.5 -> "$525.50"); em dash when null. */
export function formatMoney(value: number | null, currencyCode: string): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }
  try {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: currencyCode || 'USD',
    }).format(value);
  } catch {
    return `${value.toFixed(2)} ${currencyCode}`;
  }
}

/** Formats a share/unit quantity (up to 4 dp, no trailing zeros); em dash when null. */
export function formatQuantity(value: number | null): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }
  return new Intl.NumberFormat('en-US', { maximumFractionDigits: 4 }).format(value);
}

/** Formats a decimal rate (0.075 -> "+7.5%"); returns an em dash when null. */
export function formatPercent(rate: number | null): string {
  if (rate === null || rate === undefined || Number.isNaN(rate)) {
    return '—';
  }
  const pct = rate * 100;
  const sign = pct > 0 ? '+' : '';
  return `${sign}${pct.toFixed(1)}%`;
}

/** Up/down/neutral from a signed number. */
export function trendOf(value: number | null): 'up' | 'down' | 'neutral' {
  if (value === null || value === 0 || Number.isNaN(value)) {
    return 'neutral';
  }
  return value > 0 ? 'up' : 'down';
}

export interface SyntheticSeries {
  labels: string[];
  value: number[];
  deposits: number[];
  withdrawals: number[];
}

/**
 * TODO(perf-timeseries): the performance API returns period aggregates, not a time series.
 * Until a time-series endpoint exists, we approximate a monthly value curve by linearly
 * interpolating between the period's starting and ending balance, and spread the period's
 * total deposits/withdrawals across the months. Purely illustrative for the shell.
 */
export function buildSyntheticSeries(perf: PortfolioPerformance): SyntheticSeries {
  const start = new Date(`${perf.from}T00:00:00Z`);
  const end = new Date(`${perf.to}T00:00:00Z`);
  const months = Math.max(
    2,
    Math.min(
      13,
      (end.getUTCFullYear() - start.getUTCFullYear()) * 12 +
        (end.getUTCMonth() - start.getUTCMonth()) +
        1,
    ),
  );

  const startVal = perf.startingBalance.value;
  const endVal = perf.endingBalance.value;
  const labels: string[] = [];
  const value: number[] = [];
  const deposits: number[] = [];
  const withdrawals: number[] = [];

  const depositPerMonth = perf.contributions.deposits / months;
  const withdrawalPerMonth = perf.contributions.withdrawals / months;

  for (let i = 0; i < months; i++) {
    const point = new Date(
      Date.UTC(start.getUTCFullYear(), start.getUTCMonth() + i, 1),
    );
    labels.push(
      point.toLocaleDateString('en-US', {
        month: 'short',
        year: '2-digit',
        timeZone: 'UTC',
      }),
    );
    const t = months === 1 ? 1 : i / (months - 1);
    value.push(Math.round(startVal + (endVal - startVal) * t));
    deposits.push(Math.round(depositPerMonth));
    // Withdrawals are stored negative; show magnitude on the chart.
    withdrawals.push(Math.round(Math.abs(withdrawalPerMonth)));
  }

  return { labels, value, deposits, withdrawals };
}
