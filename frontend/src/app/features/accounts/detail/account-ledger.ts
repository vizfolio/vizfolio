import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';

import { LedgerEntry } from '../../../core/api/models/ledger.models';
import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { DataColumn, DataTable } from '../../../shared/ui/data-table/data-table';
import { DateField } from '../../../shared/ui/date-field/date-field';
import {
  formatMoney,
  formatQuantity,
} from '../../../shared/util/performance-format';

type Status = 'loading' | 'ready' | 'error';

/** Ledger tab: an account's transactions over an optional trade-date range. */
@Component({
  selector: 'app-account-ledger',
  imports: [DataTable, DateField],
  templateUrl: './account-ledger.html',
  styleUrl: './account-ledger.scss',
})
export class AccountLedger {
  readonly portfolioId = input.required<string>();
  readonly accountId = input.required<string>();

  private readonly api = inject(PortfolioApiService);

  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly status = signal<Status>('loading');
  protected readonly entries = signal<LedgerEntry[]>([]);

  protected readonly columns: DataColumn<LedgerEntry>[] = [
    { key: 'tradeDate', header: 'Date' },
    {
      key: 'type',
      header: 'Type',
      format: (_v, row) => row.sourceType ?? row.type,
    },
    { key: 'ticker', header: 'Symbol', format: (_v, row) => row.ticker ?? '—' },
    {
      key: 'quantity',
      header: 'Quantity',
      align: 'end',
      format: (_v, row) => formatQuantity(row.quantity),
    },
    {
      key: 'price',
      header: 'Price',
      align: 'end',
      format: (_v, row) => formatMoney(row.price, row.currencyCode ?? 'USD'),
    },
    {
      key: 'amount',
      header: 'Amount',
      align: 'end',
      format: (_v, row) => formatMoney(row.amount, row.currencyCode ?? 'USD'),
    },
    {
      key: 'fees',
      header: 'Fees',
      align: 'end',
      format: (_v, row) => formatMoney(row.fees, row.currencyCode ?? 'USD'),
    },
    { key: 'memo', header: 'Memo', sortable: false, format: (_v, row) => row.memo ?? '' },
  ];

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
            .getAccountLedger(portfolioId, accountId, from || undefined, to || undefined)
            .pipe(
              catchError(() => {
                this.status.set('error');
                return of<LedgerEntry[] | null>(null);
              }),
            );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((entries) => {
        if (entries) {
          this.entries.set(entries);
          this.status.set('ready');
        }
      });
  }
}
