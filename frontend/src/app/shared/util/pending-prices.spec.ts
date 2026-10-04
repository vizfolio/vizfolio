import { PortfolioPerformance } from '../../core/api/models/performance.models';
import { hasPendingPrices, performanceAwaitsPrices } from './performance-format';

describe('pending prices', () => {
  it('a balance awaits prices only when a value is missing because they are still downloading', () => {
    expect(hasPendingPrices({ missing: [{ cause: 'PricesPending' }] })).toBe(true);
    expect(hasPendingPrices({ missing: [{ cause: 'NoPrice' }] })).toBe(false);
    expect(hasPendingPrices({})).toBe(false);
    expect(hasPendingPrices(null)).toBe(false);
  });

  it('a performance result awaits prices when either end does', () => {
    const perf = {
      startingBalance: { missing: [] },
      endingBalance: { missing: [{ cause: 'PricesPending' }] },
    } as unknown as PortfolioPerformance;

    expect(performanceAwaitsPrices(perf)).toBe(true);
    expect(performanceAwaitsPrices(null)).toBe(false);
  });
});
