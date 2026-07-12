import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { AccountSummary } from '../../../core/api/models/performance.models';
import { ActivePortfolioService } from '../../../core/portfolio/active-portfolio.service';
import { AccountHistory } from './account-history';
import { AccountHoldings } from './account-holdings';
import { AccountImport } from './account-import';
import { AccountLedger } from './account-ledger';
import { AccountPerformance } from './account-performance';
import { OpeningBalanceForm } from './opening-balance-form';

export type AccountTab =
  | 'performance'
  | 'holdings'
  | 'ledger'
  | 'history'
  | 'opening-balance'
  | 'import';

interface TabDef {
  id: AccountTab;
  label: string;
}

const TABS: readonly TabDef[] = [
  { id: 'performance', label: 'Performance' },
  { id: 'holdings', label: 'Holdings' },
  { id: 'ledger', label: 'Ledger' },
  { id: 'history', label: 'History' },
  { id: 'opening-balance', label: 'Opening balance' },
  { id: 'import', label: 'Import' },
];

type LoadStatus = 'loading' | 'ready' | 'error' | 'no-portfolio';

/**
 * Account detail: a tabbed view over the account-scoped endpoints for the account identified
 * by the route `:accountId`, scoped to the currently-active portfolio.
 */
@Component({
  selector: 'app-account',
  imports: [
    RouterLink,
    AccountPerformance,
    AccountHoldings,
    AccountLedger,
    AccountHistory,
    OpeningBalanceForm,
    AccountImport,
  ],
  templateUrl: './account.html',
  styleUrl: './account.scss',
})
export class Account {
  /** Bound from the route param via withComponentInputBinding. */
  readonly accountId = input.required<string>();

  private readonly api = inject(PortfolioApiService);
  private readonly activePortfolio = inject(ActivePortfolioService);

  protected readonly portfolioId = this.activePortfolio.activeId;
  protected readonly tabs = TABS;
  protected readonly tab = signal<AccountTab>('performance');

  protected readonly account = signal<AccountSummary | null>(null);
  protected readonly status = signal<LoadStatus>('loading');

  private readonly params = computed(() => ({
    portfolioId: this.portfolioId(),
    accountId: this.accountId(),
  }));

  constructor() {
    toObservable(this.params)
      .pipe(
        switchMap(({ portfolioId, accountId }) => {
          if (!portfolioId) {
            this.status.set('no-portfolio');
            return of<AccountSummary | null>(null);
          }
          this.status.set('loading');
          return this.api.getAccount(portfolioId, accountId).pipe(
            catchError(() => {
              this.status.set('error');
              return of<AccountSummary | null>(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((account) => {
        this.account.set(account);
        if (account) {
          this.status.set('ready');
        }
      });
  }

  protected select(tab: AccountTab): void {
    this.tab.set(tab);
  }
}
