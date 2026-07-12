import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import { PortfolioPerformance } from '../../core/api/models/performance.models';
import { ActivePortfolioService } from '../../core/portfolio/active-portfolio.service';
import { DateField } from '../../shared/ui/date-field/date-field';
import { PerformanceSummary } from '../../shared/ui/performance-summary/performance-summary';

type Status = 'loading' | 'ready' | 'error' | 'no-portfolio';

/** Portfolio-level performance for the active portfolio, over an optional date range. */
@Component({
  selector: 'app-performance',
  imports: [PerformanceSummary, RouterLink, DateField],
  templateUrl: './performance.html',
  styleUrl: './performance.scss',
})
export class Performance {
  private readonly api = inject(PortfolioApiService);
  private readonly activePortfolio = inject(ActivePortfolioService);

  protected readonly portfolioName = computed(
    () => this.activePortfolio.active()?.name ?? null,
  );

  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly status = signal<Status>('loading');
  protected readonly performance = signal<PortfolioPerformance | null>(null);

  private readonly query = computed(() => ({
    portfolioId: this.activePortfolio.activeId(),
    from: this.from(),
    to: this.to(),
  }));

  constructor() {
    toObservable(this.query)
      .pipe(
        switchMap(({ portfolioId, from, to }) => {
          if (!portfolioId) {
            this.status.set('no-portfolio');
            return of<PortfolioPerformance | null>(null);
          }
          this.status.set('loading');
          return this.api.getPerformance(portfolioId, from || undefined, to || undefined).pipe(
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
