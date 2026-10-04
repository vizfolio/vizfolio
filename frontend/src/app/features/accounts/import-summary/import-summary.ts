import { Component, computed, inject, input, output, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { catchError, combineLatest, map, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  AccountImportResult,
  ImportUndoSummary,
  PortfolioImportResult,
  PortfolioImportStatus,
} from '../../../core/api/models/imports.models';
import { AccountSummary, PortfolioPerformance } from '../../../core/api/models/performance.models';
import { formatMoney, formatPercent, performanceAwaitsPrices, returnDetail } from '../../../shared/util/performance-format';
import { pollWhilePending } from '../../../shared/util/poll';
import { YOUR_RETURN_LABEL } from '../../../shared/util/reason-text';
import { impliedContributionsNote } from '../implied-contributions-note';
import { alreadyImportedNote, dateRangeText, routingNote } from '../import-text';
import { ImportWarnings } from '../import-warnings/import-warnings';
import { UndoImportConfirm } from '../undo-import/undo-import-confirm';

/** One account the import touched, with its headline once prices are in. */
interface AccountLine {
  result: AccountImportResult;
  name: string;
  routing: string | null;
  range: string | null;
  performance: PortfolioPerformance | null | undefined; // undefined: loading; null: unavailable
}

/**
 * What an import did, in plain language: the detected format; each account it went into (and how it was found),
 * the dates it covers and what was added, skipped, updated or failed (with reasons); what the parser didn't
 * understand; implied contributions; and — once background prices are in — the account's value and "Your return".
 * "Undo this import" asks first (the server's preview) and undoes in place.
 */
@Component({
  selector: 'app-import-summary',
  imports: [ImportWarnings, UndoImportConfirm],
  templateUrl: './import-summary.html',
  styleUrl: './import-summary.scss',
})
export class ImportSummary {
  readonly portfolioId = input.required<string>();
  readonly result = input.required<PortfolioImportResult>();
  /** The uploaded file's name, when known. */
  readonly fileName = input<string | null>(null);
  /** The portfolio's accounts, to name the ones the import touched. */
  readonly accounts = input<AccountSummary[]>([]);

  /** The import was undone from here. */
  readonly undone = output<ImportUndoSummary>();

  private readonly api = inject(PortfolioApiService);

  protected readonly confirmingUndo = signal(false);
  protected readonly undoneNotice = signal<string | null>(null);
  protected readonly yourReturnLabel = YOUR_RETURN_LABEL;
  protected readonly formatMoney = formatMoney;
  protected readonly formatPercent = formatPercent;
  protected readonly returnDetail = returnDetail;
  protected readonly awaitsPrices = performanceAwaitsPrices;

  protected readonly alreadyImported = computed(() => alreadyImportedNote(this.result()));
  protected readonly isNew = computed(() => this.result().status === PortfolioImportStatus.Success);
  protected readonly heading = computed(() => {
    const r = this.result();
    const format = r.parserDisplayName ?? r.sourceSystem;
    const file = this.fileName();
    return [file, format].filter(Boolean).join(' · ');
  });

  /** Each touched account's headline (value and "Your return"), refreshed while its prices download. */
  private readonly performances = toSignal(
    toObservable(computed(() => ({ portfolioId: this.portfolioId(), result: this.result() }))).pipe(
      switchMap(({ portfolioId, result }) => {
        if (result.status !== PortfolioImportStatus.Success || result.accounts.length === 0) {
          return of<Record<string, PortfolioPerformance | null>>({});
        }
        return combineLatest(
          result.accounts.map((a) =>
            pollWhilePending(
              () => this.api.getAccountPerformance(portfolioId, a.accountId),
              (perf) => performanceAwaitsPrices(perf),
            ).pipe(
              map((perf): [string, PortfolioPerformance | null] => [a.accountId, perf]),
              catchError(() => of<[string, PortfolioPerformance | null]>([a.accountId, null])),
            ),
          ),
        ).pipe(map((entries) => Object.fromEntries(entries)));
      }),
    ),
  );

  protected readonly lines = computed<AccountLine[]>(() => {
    const names = new Map(this.accounts().map((a) => [a.accountId, a.name]));
    const perf = this.performances();
    return this.result().accounts.map((result) => ({
      result,
      name: names.get(result.accountId) ?? ([result.institutionCode, result.accountNumber].filter(Boolean).join(' ') || 'Account'),
      routing: routingNote(result),
      range: dateRangeText(result.firstDate, result.lastDate),
      performance: perf !== undefined && result.accountId in perf ? perf[result.accountId] : undefined,
    }));
  });

  protected impliedNote(line: AccountLine): string | null {
    return impliedContributionsNote(line.result, line.performance?.currencyCode ?? 'USD');
  }

  protected onUndone(summary: ImportUndoSummary): void {
    this.confirmingUndo.set(false);
    this.undoneNotice.set(`Undid the import of ${summary.fileName}.`);
    this.undone.emit(summary);
  }
}
