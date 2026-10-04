import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  AccountSelection,
  ImportParser,
  PortfolioImportResult,
  PortfolioImportStatus,
  RoutingCandidate,
} from '../../../core/api/models/imports.models';
import { FileUpload } from '../../../shared/ui/file-upload/file-upload';
import { SelectField } from '../../../shared/ui/select-field/select-field';
import { ImportHistoryList } from '../import-history/import-history-list';
import { ImportSummary } from '../import-summary/import-summary';
import { accountMismatchMessage, candidateEvidence } from '../import-text';
import { parserAcceptAttr, parserFormatOptions } from '../import-format-options';

/** A file whose transactions look like another account's, held until the user decides. */
interface Misfit {
  file: File;
  selection: AccountSelection;
  here: RoutingCandidate | null;
  suggested: RoutingCandidate | null;
}

/**
 * Import tab: upload a broker file to this account. A file that names its accounts (a QFX) imports
 * only this account's statement; one that's only for other accounts is rejected (422) and the user
 * is pointed at the Accounts page instead. A file without account details whose transactions are clearly another
 * account's is held back with a choice: import it there, or here anyway. Below it, this account's import history,
 * with Undo.
 *
 * The format is auto-detected by default; the "Format" dropdown lets the user force a specific
 * parser when detection is wrong.
 */
@Component({
  selector: 'app-account-import',
  imports: [FileUpload, ImportHistoryList, ImportSummary, SelectField],
  templateUrl: './account-import.html',
  styleUrl: './account-import.scss',
})
export class AccountImport {
  readonly portfolioId = input.required<string>();
  readonly accountId = input.required<string>();

  private readonly api = inject(PortfolioApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly importing = signal(false);
  protected readonly result = signal<PortfolioImportResult | null>(null);
  protected readonly fileName = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly misfit = signal<Misfit | null>(null);
  /** Set when the last import went to another account (its name), so the summary says so. */
  protected readonly elsewhere = signal<string | null>(null);

  /** Available parsers for the Format override; '' selection means auto-detect. */
  private readonly parsers = signal<ImportParser[]>([]);
  protected readonly sourceSystem = signal('');
  protected readonly formatOptions = computed(() => parserFormatOptions(this.parsers()));
  protected readonly acceptAttr = computed(() => parserAcceptAttr(this.parsers()));
  protected readonly evidence = candidateEvidence;
  /** Bumped after each import so the history list reloads. */
  protected readonly historyKey = signal(0);

  constructor() {
    this.api
      .getImportParsers()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((parsers) => this.parsers.set(parsers));
  }

  protected onFileSelected(file: File): void {
    this.upload(file, this.accountId(), false);
  }

  /** Import the held file into the account its transactions point to. */
  protected importIntoSuggested(): void {
    const misfit = this.misfit();
    if (misfit?.suggested) {
      this.upload(misfit.file, misfit.suggested.accountId, true, misfit.suggested.name);
    }
  }

  /** Import the held file here after all. */
  protected importHereAnyway(): void {
    const misfit = this.misfit();
    if (misfit) {
      this.upload(misfit.file, this.accountId(), true);
    }
  }

  protected cancelMisfit(): void {
    this.misfit.set(null);
  }

  protected onUndone(): void {
    this.historyKey.update((n) => n + 1);
  }

  private upload(file: File, accountId: string, ignoreRoutingCheck: boolean, elsewhere: string | null = null): void {
    if (this.importing()) {
      return;
    }
    this.importing.set(true);
    this.error.set(null);
    this.result.set(null);
    this.misfit.set(null);
    this.elsewhere.set(null);
    this.fileName.set(file.name);
    this.api
      .importAccountFile(this.portfolioId(), accountId, file, this.sourceSystem() || undefined, ignoreRoutingCheck)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.importing.set(false);
          if (result.status === PortfolioImportStatus.LikelyOtherAccount && result.selections.length > 0) {
            const selection = result.selections[0];
            this.misfit.set({
              file,
              selection,
              here: selection.candidates.find((c) => c.accountId === this.accountId()) ?? null,
              suggested: selection.candidates.find((c) => c.accountId === selection.suggestedAccountId) ?? null,
            });
            return;
          }
          this.result.set(result);
          this.elsewhere.set(elsewhere);
          this.historyKey.update((n) => n + 1);
        },
        error: (err: HttpErrorResponse) => {
          this.error.set(importErrorMessage(err));
          this.importing.set(false);
        },
      });
  }
}

/** Account-scoped import error guidance. A 422 means the file is for other accounts. */
function importErrorMessage(err: HttpErrorResponse): string {
  switch (err.status) {
    case 413:
      return 'That file is too large. The import limit is 10 MB.';
    case 415:
      return 'Unsupported file type. Upload a QFX/OFX statement or a Vanguard report — or pick the format explicitly.';
    case 422:
      return accountMismatchMessage(err.error?.fileAccountNumbers);
    case 404:
      return 'Account not found. Try reselecting a portfolio.';
    default:
      return 'The import failed. Please try again.';
  }
}
