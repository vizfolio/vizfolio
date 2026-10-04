import {
  PerformanceReturn,
  PerformanceSeriesInterval,
  PortfolioPerformance,
} from '../../core/api/models/performance.models';
import { returnReasonText } from './reason-text';

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

/**
 * A return shown both ways: the big number (`value`, with `unit` saying what it is) and a line under
 * it (`detail`). For a year or more the big number is the rate **per year** and the line gives the
 * **total** it compounds to ("+12.0%" "a year", "+78.0% in total over 5.1 years"), so a figure
 * reported either way elsewhere can be compared. Under a year there's only the total. The line also
 * says when the figure is approximate, or why there's no figure at all.
 */
export interface ReturnFigure {
  value: string;
  unit: 'a year' | 'in total' | null;
  detail: string | null;
}

export function returnFigure(r: PerformanceReturn, from: string, to: string): ReturnFigure {
  if (r.rate === null) {
    return { value: formatPercent(null), unit: null, detail: returnReasonText(r.reason) };
  }

  const annual = r.annualizedRate ?? (r.basis === 'Annualized' ? r.rate : null);
  const total = r.periodRate ?? (r.basis === 'Annualized' ? null : r.rate);
  const length = formatSpan(from, to);
  const span = length ? `over ${length}` : 'over the period';
  const approximate = r.fallbackReason ? ' · approximate' : '';

  if (annual !== null) {
    const detail = total !== null ? `${formatPercent(total)} in total ${span}` : span;
    return { value: formatPercent(annual), unit: 'a year', detail: detail + approximate };
  }
  return { value: formatPercent(total), unit: 'in total', detail: span + approximate };
}

/** A return as one short phrase: "+12.0% a year", "+3.2% in total", or "—". */
export function formatReturn(r: PerformanceReturn, from: string, to: string): string {
  const f = returnFigure(r, from, to);
  return f.unit ? `${f.value} ${f.unit}` : f.value;
}

/**
 * How long a period is, in words: "5.1 years", "1 year", "8 months", "3 weeks", "12 days".
 * Dates are `YYYY-MM-DD`; null when either is missing or unreadable.
 */
export function formatSpan(from: string | null | undefined, to: string | null | undefined): string | null {
  const ms = Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`);
  if (!from || !to || Number.isNaN(ms)) {
    return null;
  }
  const days = Math.max(0, Math.round(ms / 86_400_000));
  const plural = (n: number, unit: string) => `${n} ${unit}${n === 1 ? '' : 's'}`;
  if (days >= 365) {
    const years = Math.round((days / 365.25) * 10) / 10;
    return Number.isInteger(years) ? plural(years, 'year') : `${years.toFixed(1)} years`;
  }
  if (days >= 45) {
    return plural(Math.round(days / 30.44), 'month');
  }
  if (days >= 14) {
    return plural(Math.round(days / 7), 'week');
  }
  return plural(days, 'day');
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
