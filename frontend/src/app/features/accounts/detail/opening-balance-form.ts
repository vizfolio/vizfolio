import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  OpeningBalanceHoldingInput,
  OpeningBalanceResponse,
} from '../../../core/api/models/coverage.models';

/** One editable holding row. Numeric fields are strings here and parsed on submit. */
interface HoldingRow {
  key: number;
  symbol: string;
  units: string;
  marketValue: string;
  unitPrice: string;
  costBasis: string;
  currencyCode: string;
  cusip: string;
}

let nextKey = 0;

function blankRow(): HoldingRow {
  return {
    key: nextKey++,
    symbol: '',
    units: '',
    marketValue: '',
    unitPrice: '',
    costBasis: '',
    currencyCode: '',
    cusip: '',
  };
}

/**
 * Opening-balance form: records user-supplied snapshots at a chosen date to close a
 * partial-history gap. Rows are dynamic; only rows with a symbol are submitted.
 */
@Component({
  selector: 'app-opening-balance-form',
  templateUrl: './opening-balance-form.html',
  styleUrl: './opening-balance-form.scss',
})
export class OpeningBalanceForm {
  readonly portfolioId = input.required<string>();
  readonly accountId = input.required<string>();

  private readonly api = inject(PortfolioApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly asOf = signal('');
  protected readonly defaultCurrency = signal('');
  protected readonly rows = signal<HoldingRow[]>([blankRow()]);

  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<OpeningBalanceResponse | null>(null);

  /** Rows that will actually be submitted (must have a symbol and a numeric unit count). */
  private readonly validRows = computed(() =>
    this.rows().filter((r) => r.symbol.trim().length > 0 && isFiniteNumber(r.units)),
  );

  protected readonly canSubmit = computed(
    () => !this.submitting() && this.asOf().length > 0 && this.validRows().length > 0,
  );

  protected onAsOf(event: Event): void {
    this.asOf.set((event.target as HTMLInputElement).value);
  }

  protected onDefaultCurrency(event: Event): void {
    this.defaultCurrency.set((event.target as HTMLInputElement).value);
  }

  protected updateRow(key: number, field: keyof HoldingRow, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.rows.update((rows) =>
      rows.map((r) => (r.key === key ? { ...r, [field]: value } : r)),
    );
  }

  protected addRow(): void {
    this.rows.update((rows) => [...rows, blankRow()]);
  }

  protected removeRow(key: number): void {
    this.rows.update((rows) =>
      rows.length > 1 ? rows.filter((r) => r.key !== key) : rows,
    );
  }

  protected submit(): void {
    if (!this.canSubmit()) {
      return;
    }
    const holdings: OpeningBalanceHoldingInput[] = this.validRows().map((r) => ({
      symbol: r.symbol.trim(),
      units: Number(r.units),
      marketValue: numberOrNull(r.marketValue),
      unitPrice: numberOrNull(r.unitPrice),
      costBasis: numberOrNull(r.costBasis),
      currencyCode: r.currencyCode.trim() || null,
      cusip: r.cusip.trim() || null,
    }));

    this.submitting.set(true);
    this.error.set(null);
    this.result.set(null);
    this.api
      .setOpeningBalance(this.portfolioId(), this.accountId(), {
        asOf: this.asOf(),
        defaultCurrencyCode: this.defaultCurrency().trim() || null,
        holdings,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.result.set(result);
          this.submitting.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.error.set(
            err.status === 400
              ? 'Please provide a date and at least one holding with a symbol and units.'
              : err.status === 404
                ? 'Account not found. Try reselecting a portfolio.'
                : 'Could not save the opening balance. Please try again.',
          );
          this.submitting.set(false);
        },
      });
  }
}

function isFiniteNumber(value: string): boolean {
  const trimmed = value.trim();
  return trimmed.length > 0 && Number.isFinite(Number(trimmed));
}

function numberOrNull(value: string): number | null {
  const trimmed = value.trim();
  return trimmed.length > 0 && Number.isFinite(Number(trimmed)) ? Number(trimmed) : null;
}
