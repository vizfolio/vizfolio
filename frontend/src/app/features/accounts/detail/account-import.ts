import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { ImportParser, PortfolioImportResult } from '../../../core/api/models/imports.models';
import { FileUpload } from '../../../shared/ui/file-upload/file-upload';
import { SelectField } from '../../../shared/ui/select-field/select-field';
import { parserAcceptAttr, parserFormatOptions } from '../import-format-options';

/**
 * Import tab: upload a single-account broker file to this account. Unlike the portfolio-scoped
 * import, a file that carries multiple-account metadata is rejected (422) and the user is
 * pointed at the Accounts page instead.
 *
 * The format is auto-detected by default; the "Format" dropdown lets the user force a specific
 * parser when detection is wrong.
 */
@Component({
  selector: 'app-account-import',
  imports: [FileUpload, SelectField],
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
  protected readonly error = signal<string | null>(null);

  /** Available parsers for the Format override; '' selection means auto-detect. */
  private readonly parsers = signal<ImportParser[]>([]);
  protected readonly sourceSystem = signal('');
  protected readonly formatOptions = computed(() => parserFormatOptions(this.parsers()));
  protected readonly acceptAttr = computed(() => parserAcceptAttr(this.parsers()));

  /** The single account result, if the import produced one. */
  protected readonly accountResult = computed(() => this.result()?.accounts[0] ?? null);

  constructor() {
    this.api
      .getImportParsers()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((parsers) => this.parsers.set(parsers));
  }

  protected onFileSelected(file: File): void {
    if (this.importing()) {
      return;
    }
    this.importing.set(true);
    this.error.set(null);
    this.result.set(null);
    this.api
      .importAccountFile(this.portfolioId(), this.accountId(), file, this.sourceSystem() || undefined)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.result.set(result);
          this.importing.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.error.set(importErrorMessage(err));
          this.importing.set(false);
        },
      });
  }
}

/** Account-scoped import error guidance. Note 422 means the file spans multiple accounts. */
function importErrorMessage(err: HttpErrorResponse): string {
  switch (err.status) {
    case 413:
      return 'That file is too large. The import limit is 10 MB.';
    case 415:
      return 'Unsupported file type. Upload a QFX/OFX statement or a Vanguard report — or pick the format explicitly.';
    case 422:
      return 'This file contains multiple accounts. Import it from the Accounts page instead.';
    case 404:
      return 'Account not found. Try reselecting a portfolio.';
    default:
      return 'The import failed. Please try again.';
  }
}
