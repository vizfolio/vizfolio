import {
  SAMPLE_PERFORMANCE,
  buildSyntheticSeries,
  formatCurrency,
  formatPercent,
  trendOf,
} from './dashboard.util';

describe('dashboard.util', () => {
  describe('formatCurrency', () => {
    it('formats whole-dollar amounts', () => {
      expect(formatCurrency(58750, 'USD')).toBe('$58,750');
    });

    it('falls back gracefully for an unknown currency code', () => {
      expect(formatCurrency(100, 'ZZZ')).toContain('100');
    });
  });

  describe('formatPercent', () => {
    it('adds a + sign and % for positive rates', () => {
      expect(formatPercent(0.114)).toBe('+11.4%');
    });

    it('keeps the - sign for negative rates', () => {
      expect(formatPercent(-0.05)).toBe('-5.0%');
    });

    it('shows an em dash when the rate is null', () => {
      expect(formatPercent(null)).toBe('—');
    });
  });

  describe('trendOf', () => {
    it('maps sign to up/down/neutral', () => {
      expect(trendOf(5)).toBe('up');
      expect(trendOf(-5)).toBe('down');
      expect(trendOf(0)).toBe('neutral');
      expect(trendOf(null)).toBe('neutral');
    });
  });

  describe('buildSyntheticSeries', () => {
    it('produces aligned label/value/deposit/withdrawal arrays that span the period', () => {
      const series = buildSyntheticSeries(SAMPLE_PERFORMANCE);

      expect(series.labels.length).toBeGreaterThan(1);
      expect(series.value.length).toBe(series.labels.length);
      expect(series.deposits.length).toBe(series.labels.length);
      expect(series.withdrawals.length).toBe(series.labels.length);

      // The curve starts and ends at the reported balances.
      expect(series.value.at(0)).toBe(SAMPLE_PERFORMANCE.startingBalance.value);
      expect(series.value.at(-1)).toBe(SAMPLE_PERFORMANCE.endingBalance.value);

      // Withdrawals are charted as positive magnitudes.
      expect(series.withdrawals.every((w) => w >= 0)).toBe(true);
    });
  });
});
