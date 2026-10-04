import {
  PerformanceSeriesInterval,
  PortfolioPerformance,
} from '../../core/api/models/performance.models';

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

export interface ValueSeries {
  labels: string[];
  /** Balance per point; null where a holding couldn't be valued (a gap in the line). */
  value: (number | null)[];
  deposits: number[];
  /** Withdrawals as positive magnitudes, for bars. */
  withdrawals: number[];
  /** Short description of the spacing, e.g. "Month-end values". */
  note: string;
}

const INTERVAL_NOTE: Record<PerformanceSeriesInterval, string> = {
  Weekly: 'Weekly values',
  Monthly: 'Month-end values',
  Quarterly: 'Quarter-end values',
};

/** Formats a `YYYY-MM-DD` point date for the chart axis, to suit the series spacing. */
export function formatSeriesLabel(date: string, interval: PerformanceSeriesInterval): string {
  const d = new Date(`${date}T00:00:00Z`);
  if (interval === 'Quarterly') {
    return `Q${Math.floor(d.getUTCMonth() / 3) + 1} ${String(d.getUTCFullYear()).slice(-2)}`;
  }
  const options: Intl.DateTimeFormatOptions =
    interval === 'Weekly'
      ? { month: 'short', day: 'numeric', timeZone: 'UTC' }
      : { month: 'short', year: '2-digit', timeZone: 'UTC' };
  return d.toLocaleDateString('en-US', options);
}

/** Maps the API's value-over-period series onto chart-ready arrays. */
export function buildValueSeries(perf: PortfolioPerformance): ValueSeries {
  const series = perf.series ?? { interval: 'Monthly', points: [] };
  const points = series.points;
  return {
    labels: points.map((p) => formatSeriesLabel(p.date, series.interval)),
    value: points.map((p) => (p.value === null ? null : Math.round(p.value))),
    deposits: points.map((p) => Math.round(p.deposits)),
    withdrawals: points.map((p) => Math.round(Math.abs(p.withdrawals))),
    note: INTERVAL_NOTE[series.interval],
  };
}

export interface ReturnSeries {
  labels: string[];
  /** Cumulative return in percent (0.114 -> 11.4); null where it can't be computed (a gap). */
  returnPct: (number | null)[];
  /** Cumulative investment gain in currency; null where it can't be computed (a gap). */
  gain: (number | null)[];
  note: string;
}

/** Maps the API series onto chart-ready cumulative return (%) and investment gain arrays. */
export function buildReturnSeries(perf: PortfolioPerformance): ReturnSeries {
  const series = perf.series ?? { interval: 'Monthly', points: [] };
  const points = series.points;
  return {
    labels: points.map((p) => formatSeriesLabel(p.date, series.interval)),
    returnPct: points.map((p) =>
      p.cumulativeReturn === null ? null : Math.round(p.cumulativeReturn * 10000) / 100,
    ),
    gain: points.map((p) => (p.investmentGain === null ? null : Math.round(p.investmentGain))),
    note: INTERVAL_NOTE[series.interval],
  };
}

/**
 * True when a value couldn't be computed only because prices are still downloading in the background (missing cause
 * `PricesPending`) — the view says "updating" and refreshes rather than calling the value incomplete.
 */
export function hasPendingPrices(balance: { missing?: { cause: string }[] } | null | undefined): boolean {
  return balance?.missing?.some((m) => m.cause === 'PricesPending') ?? false;
}

/** True when either end of a performance result is waiting on prices. */
export function performanceAwaitsPrices(perf: PortfolioPerformance | null): boolean {
  return perf !== null && (hasPendingPrices(perf.startingBalance) || hasPendingPrices(perf.endingBalance));
}
