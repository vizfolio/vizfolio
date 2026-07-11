import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { PortfolioImportResult } from '../../../core/api/models/imports.models';
import { FileUpload } from '../../../shared/ui/file-upload/file-upload';

/**
 * Import tab: upload a single-account broker file to this account. Unlike the portfolio-scoped
 * import, a file that carries multiple-account metadata is rejected (422) and the user is
 * pointed at the Accounts page instead.
 */
@Component({
  selector: 'app-account-import',
  imports: [FileUpload],
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

  /** The single account result, if the import produced one. */
  protected readonly accountResult = computed(() => this.result()?.accounts[0] ?? null);

  protected onFileSelected(file: File): void {
    if (this.importing()) {
      return;
    }
    this.importing.set(true);
    this.error.set(null);
    this.result.set(null);
    this.api
      .importAccountFile(this.portfolioId(), this.accountId(), file)
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
      return 'Unsupported file type. Upload a QFX/OFX or CSV export.';
    case 422:
      return 'This file contains multiple accounts. Import it from the Accounts page instead.';
    case 404:
      return 'Account not found. Try reselecting a portfolio.';
    default:
      return 'The import failed. Please try again.';
  }
}
