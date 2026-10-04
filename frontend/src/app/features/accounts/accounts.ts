import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, map, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import { HealthStatus } from '../../core/api/models/health.models';
import { AccountSummary, PortfolioPerformance } from '../../core/api/models/performance.models';
import { ActivePortfolioService } from '../../core/portfolio/active-portfolio.service';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { formatMoney, formatPercent } from '../../shared/util/performance-format';
import { YOUR_RETURN_LABEL } from '../../shared/util/reason-text';
import { ImportDropZone } from './import-drop-zone/import-drop-zone';
import { ImportHistoryList } from './import-history/import-history-list';
import { ImportOnboarding } from './import-onboarding/import-onboarding';

type ListStatus = 'idle' | 'loading' | 'ready' | 'error';

/** Fields of the add-account form. */
interface AccountForm {
  name: string;
  institutionCode: string;
  accountNumber: string;
  accountType: string;
}

const EMPTY_FORM: AccountForm = {
  name: '',
  institutionCode: '',
  accountNumber: '',
  accountType: '',
};

/** Value, this year's "Your return" and data health for one account in the list. */
interface AccountOverview {
  value: string;
  yourReturn: string;
  health: HealthStatus | null;
}

const HEALTH_TEXT: Record<HealthStatus, string> = {
  Healthy: 'Data looks complete',
  Info: 'Data has notes worth a look',
  NeedsAttention: 'Data needs attention',
};

/**
 * Accounts for the active portfolio: the list (with each account's value, this year's return and a data-health dot),
 * the one drop zone for broker files (which finds or creates each file's account), a manual add-account form, and
 * the import history.
 */
@Component({
  selector: 'app-accounts',
  imports: [EmptyState, ImportDropZone, ImportHistoryList, ImportOnboarding, RouterLink],
  templateUrl: './accounts.html',
  styleUrl: './accounts.scss',
})
export class Accounts {
  private readonly api = inject(PortfolioApiService);
  private readonly activePortfolio = inject(ActivePortfolioService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly activeId = this.activePortfolio.activeId;
  protected readonly activeName = computed(() => this.activePortfolio.active()?.name ?? null);
  protected readonly yourReturnLabel = YOUR_RETURN_LABEL;
  /** Bumped after each import so the history list reloads. */
  protected readonly historyKey = signal(0);

  protected readonly accounts = signal<AccountSummary[]>([]);
  protected readonly listStatus = signal<ListStatus>('idle');
  protected readonly overview = signal<Record<string, AccountOverview>>({});

  // Add-account form state.
  protected readonly form = signal<AccountForm>({ ...EMPTY_FORM });
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly canCreate = computed(
    () =>
      !this.submitting() &&
      this.form().name.trim().length > 0 &&
      this.form().institutionCode.trim().length > 0 &&
      this.form().accountNumber.trim().length > 0,
  );

  constructor() {
    // (Re)load accounts whenever the active portfolio changes.
    toObservable(this.activeId)
      .pipe(
        switchMap((portfolioId) => {
          if (!portfolioId) {
            this.listStatus.set('idle');
            return of<AccountSummary[] | null>([]);
          }
          this.listStatus.set('loading');
          return this.api.getAccounts(portfolioId).pipe(
            catchError(() => {
              this.listStatus.set('error');
              return of<AccountSummary[] | null>(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((accounts) => {
        if (accounts === null) {
          return; // error already recorded
        }
        this.accounts.set(accounts);
        if (this.activeId()) {
          this.listStatus.set('ready');
        }
        this.loadOverview();
      });
  }

  protected healthText(status: HealthStatus): string {
    return HEALTH_TEXT[status];
  }

  protected updateField(field: keyof AccountForm, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.form.update((f) => ({ ...f, [field]: value }));
  }

  /** Create an account under the active portfolio and prepend it to the list. */
  protected create(): void {
    const portfolioId = this.activeId();
    if (!portfolioId || !this.canCreate()) {
      return;
    }
    const form = this.form();
    this.submitting.set(true);
    this.formError.set(null);
    this.api
      .createAccount(portfolioId, {
        name: form.name.trim(),
        institutionCode: form.institutionCode.trim(),
        accountNumber: form.accountNumber.trim(),
        accountType: form.accountType.trim() || null,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.accounts.update((list) => [created, ...list]);
          this.form.set({ ...EMPTY_FORM });
          this.submitting.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.formError.set(
            err.status === 409
              ? 'An account with that institution and number already exists in this portfolio.'
              : 'Could not create the account. Please try again.',
          );
          this.submitting.set(false);
        },
      });
  }

  /** After an import or undo: refresh the list, its figures and the history. */
  protected onImportsChanged(): void {
    this.historyKey.update((n) => n + 1);
    this.reload();
  }

  /** One-off refetch of the account list (after a mutation that the server performed, e.g. an undo). */
  protected reload(): void {
    const portfolioId = this.activeId();
    if (!portfolioId) {
      return;
    }
    this.api
      .getAccounts(portfolioId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((accounts) => {
        this.accounts.set(accounts);
        this.loadOverview();
      });
  }

  protected accountTypeLabel(type: string | null): string {
    return type && type.trim().length > 0 ? type : '—';
  }

  /** Each account's value and year-to-date "Your return" (in parallel), and every account's data health (one call). */
  private loadOverview(): void {
    const portfolioId = this.activeId();
    const accounts = this.accounts();
    if (!portfolioId || accounts.length === 0) {
      this.overview.set({});
      return;
    }
    const yearStart = `${new Date().getFullYear()}-01-01`;
    const performances = forkJoin(
      accounts.map((a) =>
        this.api.getAccountPerformance(portfolioId, a.accountId, yearStart).pipe(
          catchError(() => of<PortfolioPerformance | null>(null)),
          map((perf) => [a.accountId, perf] as const),
        ),
      ),
    );
    const health = this.api.getPortfolioHealth(portfolioId).pipe(catchError(() => of(null)));

    forkJoin([performances, health])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(([perfs, report]) => {
        const statuses = new Map(report?.accounts.map((a) => [a.accountId, a.status]) ?? []);
        const next: Record<string, AccountOverview> = {};
        for (const [accountId, perf] of perfs) {
          next[accountId] = {
            value: perf ? formatMoney(perf.endingBalance.value, perf.currencyCode) : '—',
            yourReturn: perf ? formatPercent(perf.returns.moneyWeighted.rate) : '—',
            health: statuses.get(accountId) ?? null,
          };
        }
        this.overview.set(next);
      });
  }
}
