import { Component, computed, input, signal } from '@angular/core';

/** A row is a plain record; columns pick fields out of it by key. */
export type DataRow = Record<string, unknown>;

export type SortDirection = 'asc' | 'desc';

/** Describes one column of a {@link DataTable}. */
export interface DataColumn<T extends DataRow = DataRow> {
  /** Property on the row to read. */
  key: keyof T & string;
  /** Column header text. */
  header: string;
  /** Cell alignment; use `end` for numeric columns. Defaults to `start`. */
  align?: 'start' | 'end';
  /** Whether the column can be sorted. Defaults to true. */
  sortable?: boolean;
  /** Optional display formatter; receives the cell value and the whole row. */
  format?: (value: T[keyof T & string], row: T) => string;
}

/**
 * Presentational, client-side sortable table. Fully driven by inputs — no data fetching —
 * so it can back the ledger, holdings, and any other tabular view. Sorting is stable and
 * type-aware (numbers numerically, everything else by locale string compare; ISO date
 * strings therefore sort correctly).
 */
@Component({
  selector: 'app-data-table',
  templateUrl: './data-table.html',
  styleUrl: './data-table.scss',
})
export class DataTable<T extends DataRow = DataRow> {
  readonly columns = input.required<DataColumn<T>[]>();
  readonly rows = input.required<T[]>();
  /** Shown in place of the table body when there are no rows. */
  readonly emptyMessage = input('No data.');
  /** Optional accessible caption for the table. */
  readonly caption = input<string | null>(null);

  private readonly sortKey = signal<string | null>(null);
  private readonly sortDir = signal<SortDirection>('asc');

  protected readonly sortedRows = computed<T[]>(() => {
    const key = this.sortKey();
    const rows = this.rows();
    if (!key) {
      return rows;
    }
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    // Copy before sorting so we never mutate the caller's array.
    return [...rows].sort((a, b) => compare(a[key], b[key]) * dir);
  });

  protected isSortable(column: DataColumn<T>): boolean {
    return column.sortable !== false;
  }

  /** aria-sort value for a header cell. */
  protected ariaSort(column: DataColumn<T>): 'ascending' | 'descending' | 'none' {
    if (this.sortKey() !== column.key) {
      return 'none';
    }
    return this.sortDir() === 'asc' ? 'ascending' : 'descending';
  }

  /** Toggles sort on a column: first click ascending, second descending. */
  protected toggleSort(column: DataColumn<T>): void {
    if (!this.isSortable(column)) {
      return;
    }
    if (this.sortKey() === column.key) {
      this.sortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortKey.set(column.key);
      this.sortDir.set('asc');
    }
  }

  protected cell(column: DataColumn<T>, row: T): string {
    const value = row[column.key];
    if (column.format) {
      return column.format(value, row);
    }
    return value === null || value === undefined ? '' : String(value);
  }
}

/** Compares two cell values: numbers numerically, nullish last, otherwise by locale string. */
function compare(a: unknown, b: unknown): number {
  if (a === b) {
    return 0;
  }
  if (a === null || a === undefined) {
    return 1;
  }
  if (b === null || b === undefined) {
    return -1;
  }
  if (typeof a === 'number' && typeof b === 'number') {
    return a - b;
  }
  return String(a).localeCompare(String(b), undefined, { numeric: true });
}
