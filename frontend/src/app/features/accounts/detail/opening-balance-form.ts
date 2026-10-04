import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  OpeningBalanceHoldingInput,
  OpeningBalanceResponse,
  OpeningPosition,
  OpeningPositionsResponse,
} from '../../../core/api/models/coverage.models';
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

/** Symbol the backend reads as the account's cash in an opening balance. */
export const CASH_SYMBOL = '$CASH';

/**
 * Seeds a row from a derived starting position. A position held before the imported history is prefilled with
 * what the broker's statement implies (the user just confirms or corrects it); one that can't be derived because
 * the ledger and the statement disagree is left blank for the user to supply.
 */
function rowFromPosition(position: OpeningPosition, symbol: string): HoldingRow {
  if (position.class !== 'PreHistory') {
    return { ...blankRow(), symbol };
  }
  return {
    ...blankRow(),
    symbol,
    units: numberToField(position.quantity),
    marketValue: numberToField(position.marketValue),
    unitPrice: numberToField(position.unitPrice),
  };
}

/** The positions worth showing: held before the history, or impossible to derive. */
function rowsFrom(response: OpeningPositionsResponse): HoldingRow[] {
  const needsShowing = (p: OpeningPosition) => p.class === 'PreHistory' || p.class === 'Inconsistent';
  const rows = response.holdings
    .filter((p) => p.symbol && needsShowing(p))
    .map((p) => rowFromPosition(p, p.symbol ?? ''));
  if (needsShowing(response.cash)) {
    rows.push(rowFromPosition(response.cash, CASH_SYMBOL));
  }
  return rows;
}

function numberToField(value: number | null): string {
  return value === null ? '' : String(value);
}

/**
 * Opening-balance form: corrects the account's starting positions when the ones Vizfolio derives
 * from the broker's statements are wrong or can't be derived. Rows are dynamic; only rows with a
 * symbol are submitted. The cash row uses the `$CASH` symbol.
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
    // Seed the form with the starting positions Vizfolio derived from the broker's statements: the day
    // before the first transaction, the positions held before the imported history (prefilled), and any it
    // couldn't derive (blank). A failed prefetch falls back to the blank form so balances can still be
    // entered by hand.
    toObservable(this.accountId)
      .pipe(
        switchMap((accountId) => {
          this.prefilling.set(true);
          return this.api
            .getOpeningPositions(this.portfolioId(), accountId)
            .pipe(catchError(() => of<OpeningPositionsResponse | null>(null)));
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((response) => {
        if (response?.asOf) {
          this.asOf.set(response.asOf);
        }
        const prefilled = response ? rowsFrom(response) : [];
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
                : 'Could not save the starting positions. Please try again.',
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
