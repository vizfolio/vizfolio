import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { AccountSummary } from '../../../core/api/models/performance.models';
import { ActivePortfolioService } from '../../../core/portfolio/active-portfolio.service';
import { AccountHealth } from './account-health';
import { AccountHoldings } from './account-holdings';
import { AccountImport } from './account-import';
import { AccountLedger } from './account-ledger';
import { AccountPerformance } from './account-performance';
import { OpeningBalanceForm } from './opening-balance-form';

export type AccountTab =
  | 'performance'
  | 'holdings'
  | 'ledger'
  | 'health'
  | 'import'
  | 'starting-positions';

interface TabDef {
  id: AccountTab;
  label: string;
}

const TABS: readonly TabDef[] = [
  { id: 'performance', label: 'Performance' },
  { id: 'holdings', label: 'Holdings' },
  { id: 'ledger', label: 'Ledger' },
  { id: 'health', label: 'Data health' },
  { id: 'import', label: 'Import' },
  // Rarely needed: starting positions are derived from statements.
  { id: 'starting-positions', label: 'Adjust starting positions' },
];

function isTab(value: string | undefined): value is AccountTab {
  return TABS.some((t) => t.id === value);
}

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
    AccountHealth,
    OpeningBalanceForm,
    AccountImport,
  ],
  templateUrl: './account.html',
  styleUrl: './account.scss',
})
export class Account {
  /** Bound from the route param via withComponentInputBinding. */
  readonly accountId = input.required<string>();
  /** Optional `?tab=` query parameter (e.g. `health` from the accounts list), bound the same way. */
  readonly initialTab = input<string | undefined>(undefined, { alias: 'tab' });

  private readonly api = inject(PortfolioApiService);
  private readonly activePortfolio = inject(ActivePortfolioService);

  protected readonly portfolioId = this.activePortfolio.activeId;
  protected readonly tabs = TABS;
  protected readonly tab = linkedSignal<AccountTab>(() => {
    const requested = this.initialTab();
    return isTab(requested) ? requested : 'performance';
  });

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

  /** A data health action that lives on another tab. */
  protected onHealthNavigate(kind: 'AdjustStartingPosition' | 'ReimportFile'): void {
    this.select(kind === 'AdjustStartingPosition' ? 'starting-positions' : 'import');
  }
}
