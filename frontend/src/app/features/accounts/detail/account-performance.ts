import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { PortfolioPerformance } from '../../../core/api/models/performance.models';
import { DateField } from '../../../shared/ui/date-field/date-field';
import { PerformanceSummary } from '../../../shared/ui/performance-summary/performance-summary';

type Status = 'loading' | 'ready' | 'error';

/** Performance tab: account-scoped returns over an optional date range. */
@Component({
  selector: 'app-account-performance',
  imports: [PerformanceSummary, DateField],
  templateUrl: './account-performance.html',
  styleUrl: './account-performance.scss',
})
export class AccountPerformance {
  readonly portfolioId = input.required<string>();
  readonly accountId = input.required<string>();

  private readonly api = inject(PortfolioApiService);

  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly status = signal<Status>('loading');
  protected readonly performance = signal<PortfolioPerformance | null>(null);

  private readonly query = computed(() => ({
    portfolioId: this.portfolioId(),
    accountId: this.accountId(),
    from: this.from(),
    to: this.to(),
  }));

  constructor() {
    toObservable(this.query)
      .pipe(
        switchMap(({ portfolioId, accountId, from, to }) => {
          this.status.set('loading');
          return this.api
            .getAccountPerformance(portfolioId, accountId, from || undefined, to || undefined)
            .pipe(
              catchError(() => {
                this.status.set('error');
                return of<PortfolioPerformance | null>(null);
              }),
            );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((perf) => {
        this.performance.set(perf);
        if (perf) {
          this.status.set('ready');
        }
      });
  }
}
