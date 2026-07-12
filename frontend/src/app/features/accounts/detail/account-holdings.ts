import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';

import { HoldingRow } from '../../../core/api/models/holdings.models';
import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { DataColumn, DataTable } from '../../../shared/ui/data-table/data-table';
import {
  formatMoney,
  formatQuantity,
} from '../../../shared/util/performance-format';

type Status = 'loading' | 'ready' | 'error';

/** Holdings tab: an account's positions, each valued from its latest snapshot. */
@Component({
  selector: 'app-account-holdings',
  imports: [DataTable],
  templateUrl: './account-holdings.html',
  styleUrl: './account-holdings.scss',
})
export class AccountHoldings {
  readonly portfolioId = input.required<string>();
  readonly accountId = input.required<string>();

  private readonly api = inject(PortfolioApiService);

  protected readonly status = signal<Status>('loading');
  protected readonly holdings = signal<HoldingRow[]>([]);

  /** The reporting currency to format totals in — the first holding's, defaulting to USD. */
  protected readonly currencyCode = computed(
    () => this.holdings().find((h) => h.currencyCode)?.currencyCode ?? 'USD',
  );

  protected readonly totalValue = computed(() =>
    this.holdings().reduce((sum, h) => sum + (h.marketValue ?? 0), 0),
  );

  protected readonly totalLabel = computed(() =>
    formatMoney(this.totalValue(), this.currencyCode()),
  );

  protected readonly missingCount = computed(
    () => this.holdings().filter((h) => !h.hasSnapshot).length,
  );

  protected readonly columns: DataColumn<HoldingRow>[] = [
    { key: 'symbol', header: 'Symbol', format: (_v, row) => row.symbol ?? '—' },
    { key: 'name', header: 'Name', format: (_v, row) => row.name ?? '—' },
    {
      key: 'quantity',
      header: 'Quantity',
      align: 'end',
      format: (_v, row) => formatQuantity(row.quantity),
    },
    {
      key: 'unitPrice',
      header: 'Unit price',
      align: 'end',
      format: (_v, row) => formatMoney(row.unitPrice, row.currencyCode ?? 'USD'),
    },
    {
      key: 'marketValue',
      header: 'Market value',
      align: 'end',
      format: (_v, row) => formatMoney(row.marketValue, row.currencyCode ?? 'USD'),
    },
    {
      key: 'costBasis',
      header: 'Cost basis',
      align: 'end',
      format: (_v, row) => formatMoney(row.costBasis, row.currencyCode ?? 'USD'),
    },
    {
      key: 'gainLoss',
      header: 'Gain / loss',
      align: 'end',
      format: (_v, row) => formatMoney(row.gainLoss, row.currencyCode ?? 'USD'),
    },
    {
      key: 'snapshotAsOf',
      header: 'As of',
      align: 'end',
      format: (_v, row) => row.snapshotAsOf ?? '—',
    },
  ];

  private readonly query = computed(() => ({
    portfolioId: this.portfolioId(),
    accountId: this.accountId(),
  }));

  constructor() {
    toObservable(this.query)
      .pipe(
        switchMap(({ portfolioId, accountId }) => {
          this.status.set('loading');
          return this.api.getAccountHoldings(portfolioId, accountId).pipe(
            catchError(() => {
              this.status.set('error');
              return of<HoldingRow[] | null>(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((holdings) => {
        if (holdings) {
          this.holdings.set(holdings);
          this.status.set('ready');
        }
      });
  }
}
