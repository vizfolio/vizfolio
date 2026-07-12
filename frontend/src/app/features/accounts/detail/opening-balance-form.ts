import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, map, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  OpeningBalanceHoldingInput,
  OpeningBalanceResponse,
} from '../../../core/api/models/coverage.models';
import { HoldingRow as HoldingDto } from '../../../core/api/models/holdings.models';
import { DateField } from '../../../shared/ui/date-field/date-field';

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
 * Seeds a row from a known holding: identity fields (symbol/currency/CUSIP) plus any values
 * already recorded in the snapshot at the opening date, so the user sees existing balances and
 * can complete or correct them. Missing values stay blank for the user to supply.
 */
function rowFromHolding(holding: HoldingDto): HoldingRow {
  return {
    ...blankRow(),
    symbol: holding.symbol ?? '',
    currencyCode: holding.currencyCode ?? '',
    cusip: holding.cusip ?? '',
    units: numberToField(holding.quantity),
    marketValue: numberToField(holding.marketValue),
    unitPrice: numberToField(holding.unitPrice),
    costBasis: numberToField(holding.costBasis),
  };
}

function numberToField(value: number | null): string {
  return value === null ? '' : String(value);
}

/**
 * Opening-balance form: records user-supplied snapshots at a chosen date to close a
 * partial-history gap. Rows are dynamic; only rows with a symbol are submitted.
 */
@Component({
  selector: 'app-opening-balance-form',
  imports: [DateField],
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

  protected readonly prefilling = signal(false);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<OpeningBalanceResponse | null>(null);

  constructor() {
    // Seed the form from the account's history gap: the suggested opening date and a row per
    // holding valued at that date (existing snapshot values included). A failed prefetch falls
    // back to the blank form so the user can still enter balances by hand.
    toObservable(this.accountId)
      .pipe(
        switchMap((accountId) => {
          this.prefilling.set(true);
          return this.api.getHistoryCoverage(this.portfolioId(), accountId).pipe(
            switchMap((coverage) => {
              const date = coverage.suggestedOpeningDate;
              if (!date) {
                return of({ date: null as string | null, holdings: [] as HoldingDto[] });
              }
              return this.api
                .getAccountHoldings(this.portfolioId(), accountId, date)
                .pipe(map((holdings) => ({ date, holdings })));
            }),
            catchError(() =>
              of({ date: null as string | null, holdings: [] as HoldingDto[] }),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ date, holdings }) => {
        if (date) {
          this.asOf.set(date);
        }
        // Show every holding valued at the opening date, with any existing snapshot values,
        // so each row carries a complete/incomplete status the user can act on.
        const prefilled = holdings.filter((h) => h.symbol).map(rowFromHolding);
        this.rows.set(prefilled.length > 0 ? prefilled : [blankRow()]);
        this.prefilling.set(false);
      });
  }

  /**
   * A row's opening balance is complete when it will produce a usable market value: a symbol,
   * a quantity, and either an explicit market value or a unit price (the backend derives
   * market value from units × unit price). Mirrors the backend completeness rule so this
   * screen, the History tab, and Performance agree.
   */
  protected isRowComplete(row: HoldingRow): boolean {
    return (
      row.symbol.trim().length > 0 &&
      isFiniteNumber(row.units) &&
      (numberOrNull(row.marketValue) !== null || numberOrNull(row.unitPrice) !== null)
    );
  }

  /** Rows that name a holding (blank scratch rows don't count toward the completeness tally). */
  protected readonly namedRows = computed(() =>
    this.rows().filter((r) => r.symbol.trim().length > 0),
  );

  protected readonly completeCount = computed(
    () => this.namedRows().filter((r) => this.isRowComplete(r)).length,
  );

  /** Rows that will actually be submitted (must have a symbol and a numeric unit count). */
  private readonly validRows = computed(() =>
    this.rows().filter((r) => r.symbol.trim().length > 0 && isFiniteNumber(r.units)),
  );

  protected readonly canSubmit = computed(
    () => !this.submitting() && this.asOf().length > 0 && this.validRows().length > 0,
  );

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
