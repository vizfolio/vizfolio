import { Component, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { HistoryCoverageResponse } from '../../../core/api/models/coverage.models';

type Status = 'loading' | 'ready' | 'error';

/**
 * History & data quality tab: reports the account's transaction-vs-snapshot coverage and,
 * when there's a fillable gap, invites the user to record an opening balance.
 */
@Component({
  selector: 'app-account-history',
  templateUrl: './account-history.html',
  styleUrl: './account-history.scss',
})
export class AccountHistory {
  readonly portfolioId = input.required<string>();
  readonly accountId = input.required<string>();

  /** Raised when the user chooses to fill a history gap (parent switches to opening-balance). */
  readonly fillGap = output<void>();

  private readonly api = inject(PortfolioApiService);

  protected readonly status = signal<Status>('loading');
  protected readonly coverage = signal<HistoryCoverageResponse | null>(null);

  constructor() {
    toObservable(this.accountId)
      .pipe(
        switchMap((accountId) => {
          this.status.set('loading');
          return this.api.getHistoryCoverage(this.portfolioId(), accountId).pipe(
            catchError(() => {
              this.status.set('error');
              return of<HistoryCoverageResponse | null>(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((coverage) => {
        this.coverage.set(coverage);
        if (coverage) {
          this.status.set('ready');
        }
      });
  }

  protected orDash(value: string | null): string {
    return value ?? '—';
  }
}
