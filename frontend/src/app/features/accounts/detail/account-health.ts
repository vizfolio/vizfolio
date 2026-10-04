import { Component, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, combineLatest, map, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { HistoryCoverageResponse } from '../../../core/api/models/coverage.models';
import { DataHealthReport } from '../../../core/api/models/health.models';
import { pollWhilePending } from '../../../shared/util/poll';
import { HealthList, HealthNavigation } from '../health-list/health-list';
import { formatDay } from '../import-text';

type Status = 'loading' | 'ready' | 'error';

/** True while some finding may clear on its own once background prices arrive. */
function awaitsPrices(report: DataHealthReport | null): boolean {
  return report?.findings.some((f) => f.details.pricesPending) ?? false;
}

/**
 * Data health tab: everything that leaves this account's numbers blank, approximate or assumed, with what to do about
 * each (see {@link HealthList}), plus the history coverage facts behind it. Re-checks while prices download.
 */
@Component({
  selector: 'app-account-health',
  imports: [HealthList],
  templateUrl: './account-health.html',
  styleUrl: './account-health.scss',
})
export class AccountHealth {
  readonly portfolioId = input.required<string>();
  readonly accountId = input.required<string>();

  /** A finding's action needs another tab (adjust starting positions, import a file). */
  readonly navigate = output<HealthNavigation>();

  private readonly api = inject(PortfolioApiService);

  protected readonly status = signal<Status>('loading');
  protected readonly report = signal<DataHealthReport | null>(null);
  protected readonly coverage = signal<HistoryCoverageResponse | null>(null);
  protected readonly formatDay = formatDay;
  private readonly reloads = signal(0);

  constructor() {
    const request = computed(() => ({ portfolioId: this.portfolioId(), accountId: this.accountId(), n: this.reloads() }));
    toObservable(request)
      .pipe(
        switchMap(({ portfolioId, accountId }) => {
          this.status.set('loading');
          const health = pollWhilePending(() => this.api.getAccountHealth(portfolioId, accountId), awaitsPrices);
          const coverage = this.api.getHistoryCoverage(portfolioId, accountId).pipe(catchError(() => of(null)));
          return combineLatest([health, coverage]).pipe(
            map(([report, cov]) => ({ report, cov })),
            catchError(() => of(null)),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((loaded) => {
        if (!loaded) {
          this.status.set('error');
          return;
        }
        this.report.set(loaded.report);
        this.coverage.set(loaded.cov);
        this.status.set('ready');
      });
  }

  /** Prices were requested: check again (and keep checking while they download). */
  protected recheck(): void {
    this.reloads.update((n) => n + 1);
  }

  protected orDash(value: string | null): string {
    return value ? formatDay(value) : '—';
  }
}

