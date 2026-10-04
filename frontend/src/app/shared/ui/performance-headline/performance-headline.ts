import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import {
  PerformanceBalance,
  PortfolioPerformance,
} from '../../../core/api/models/performance.models';
import {
  formatCurrency,
  formatPercent,
  hasPendingPrices,
  returnDetail,
} from '../../util/performance-format';
import {
  INVESTMENT_RETURN_HINT,
  INVESTMENT_RETURN_LABEL,
  YOUR_RETURN_HINT,
  YOUR_RETURN_LABEL,
  missingText,
} from '../../util/reason-text';
import { CompletenessBadge } from '../completeness-badge/completeness-badge';
import { StatCard } from '../stat-card/stat-card';

/**
 * The headline row shared by the dashboard and the portfolio / account Performance pages, so the
 * cards and their order are identical everywhere: Balance (ending value, where the period started,
 * and a completeness badge) · Your return (money-weighted, featured) · Investment return
 * (time-weighted) · Net contributions (with gross in / out underneath at account scope).
 */
@Component({
  selector: 'app-performance-headline',
  imports: [CompletenessBadge, RouterLink, StatCard],
  templateUrl: './performance-headline.html',
  styleUrl: './performance-headline.scss',
})
export class PerformanceHeadline {
  readonly performance = input.required<PortfolioPerformance>();
  /** "Balance" on Performance pages; the dashboard calls it "Portfolio value". */
  readonly balanceLabel = input('Balance');
  /**
   * Show gross deposits / withdrawals under net contributions. Off at portfolio scope, where internal
   * transfers count as both a deposit and a withdrawal and inflate the gross figures (only Net is
   * meaningful there — see docs/performance-api.md).
   */
  readonly showGrossFlows = input(true);
  /** Where "Review data health" goes when something couldn't be valued (router commands), or null for no link. */
  readonly healthLink = input<string[] | null>(null);
  readonly healthQuery = input<Record<string, string> | null>(null);

  protected readonly yourReturnLabel = YOUR_RETURN_LABEL;
  protected readonly yourReturnHint = YOUR_RETURN_HINT;
  protected readonly investmentReturnLabel = INVESTMENT_RETURN_LABEL;
  protected readonly investmentReturnHint = INVESTMENT_RETURN_HINT;
  protected readonly pendingPrices = hasPendingPrices;

  private readonly currency = computed(() => this.performance().currencyCode || 'USD');

  protected money(value: number): string {
    return formatCurrency(value, this.currency());
  }

  protected readonly balanceDetail = computed(
    () => `from ${this.money(this.performance().startingBalance.value)} at the start`,
  );

  /**
   * The balance the completeness badge describes: whichever end of the period is worse off — an
   * estimate over one still waiting on prices, over a fully valued one (the ending balance).
   */
  protected readonly balanceStatus = computed<PerformanceBalance>(() => {
    const p = this.performance();
    const rank = (b: PerformanceBalance) => (b.isComplete ? 0 : hasPendingPrices(b) ? 1 : 2);
    return rank(p.startingBalance) > rank(p.endingBalance) ? p.startingBalance : p.endingBalance;
  });

  /** Under the cards: what couldn't be valued at the worse end of the period, unless it's only prices downloading. */
  protected readonly missingNote = computed(() => {
    const p = this.performance();
    const b = this.balanceStatus();
    if (b.isComplete || hasPendingPrices(b)) {
      return null;
    }
    const when = b === p.startingBalance ? 'at the start of the period' : 'at the end of the period';
    return missingText(b.missing, when);
  });

  protected readonly mwrValue = computed(() =>
    formatPercent(this.performance().returns.moneyWeighted.rate),
  );
  protected readonly mwrDetail = computed(() => returnDetail(this.performance().returns.moneyWeighted));

  protected readonly twrValue = computed(() =>
    formatPercent(this.performance().returns.timeWeighted.rate),
  );
  protected readonly twrDetail = computed(() => returnDetail(this.performance().returns.timeWeighted));

  protected readonly contributionsDetail = computed(() => {
    if (!this.showGrossFlows()) {
      return null;
    }
    const c = this.performance().contributions;
    return `${this.money(c.deposits)} in · ${this.money(Math.abs(c.withdrawals))} out`;
  });
}
