import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import { ImportParser, PortfolioImportResult } from '../../core/api/models/imports.models';
import { AccountSummary } from '../../core/api/models/performance.models';
import { ActivePortfolioService } from '../../core/portfolio/active-portfolio.service';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { FileUpload } from '../../shared/ui/file-upload/file-upload';
import { SelectField } from '../../shared/ui/select-field/select-field';
import { parserAcceptAttr, parserFormatOptions } from './import-format-options';

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

/**
 * Accounts for the active portfolio: list, add, and bulk-import (portfolio-scoped, i.e. a
 * broker file that carries account metadata and may create several accounts at once).
 */
@Component({
  selector: 'app-accounts',
  imports: [EmptyState, FileUpload, RouterLink, SelectField],
  templateUrl: './accounts.html',
  styleUrl: './accounts.scss',
})
export class Accounts {
  private readonly api = inject(PortfolioApiService);
  private readonly activePortfolio = inject(ActivePortfolioService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly activeId = this.activePortfolio.activeId;
  protected readonly activeName = computed(() => this.activePortfolio.active()?.name ?? null);

  protected readonly accounts = signal<AccountSummary[]>([]);
  protected readonly listStatus = signal<ListStatus>('idle');

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

  // Import state.
  protected readonly importing = signal(false);
  protected readonly importResult = signal<PortfolioImportResult | null>(null);
  protected readonly importError = signal<string | null>(null);

  // Format override; '' selection means auto-detect.
  private readonly parsers = signal<ImportParser[]>([]);
  protected readonly sourceSystem = signal('');
  protected readonly formatOptions = computed(() => parserFormatOptions(this.parsers()));
  protected readonly acceptAttr = computed(() => parserAcceptAttr(this.parsers()));

  constructor() {
    this.api
      .getImportParsers()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((parsers) => this.parsers.set(parsers));

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
      });
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

  /** Upload a portfolio-scoped broker file; refresh the list on success. */
  protected onFileSelected(file: File): void {
    const portfolioId = this.activeId();
    if (!portfolioId || this.importing()) {
      return;
    }
    this.importing.set(true);
    this.importError.set(null);
    this.importResult.set(null);
    this.api
      .importPortfolioFile(portfolioId, file, this.sourceSystem() || undefined)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.importResult.set(result);
          this.importing.set(false);
          this.reload();
        },
        error: (err: HttpErrorResponse) => {
          this.importError.set(importErrorMessage(err));
          this.importing.set(false);
        },
      });
  }

  /** One-off refetch of the account list (after a mutation that the server performed). */
  private reload(): void {
    const portfolioId = this.activeId();
    if (!portfolioId) {
      return;
    }
    this.api
      .getAccounts(portfolioId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((accounts) => this.accounts.set(accounts));
  }

  protected accountTypeLabel(type: string | null): string {
    return type && type.trim().length > 0 ? type : '—';
  }
}

/** Maps import HTTP failures to user-facing guidance. */
function importErrorMessage(err: HttpErrorResponse): string {
  switch (err.status) {
    case 413:
      return 'That file is too large. The import limit is 10 MB.';
    case 415:
      return 'Unsupported file type. Upload a QFX/OFX statement or a Vanguard report — or pick the format explicitly.';
    case 422:
      return 'This file has no account metadata. Import it from a specific account instead.';
    case 404:
      return 'Portfolio not found. Try reselecting a portfolio.';
    default:
      return 'The import failed. Please try again.';
  }
}
