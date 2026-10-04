import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  ImportBatchAccount,
  ImportBatchItem,
  ImportHistory,
  ImportUndoSummary,
} from '../../../core/api/models/imports.models';
import { formatImportedAt, undoPreviewLines } from '../import-text';
import { ImportWarnings } from '../import-warnings/import-warnings';

type LoadStatus = 'loading' | 'ready' | 'error';

/** An undo the user asked for: its preview loads first, then they confirm or cancel. */
interface PendingUndo {
  batch: ImportBatchItem;
  preview: ImportUndoSummary | null;
  error: string | null;
}

/**
 * The portfolio's imports, newest first — what each file did and what it didn't understand — with an Undo for any
 * import still in effect. Undo asks first, showing exactly what it will remove or restore (the server's preview).
 * On an account page, `accountId` limits the list to imports into that account.
 */
@Component({
  selector: 'app-import-history-list',
  imports: [ImportWarnings],
  templateUrl: './import-history-list.html',
  styleUrl: './import-history-list.scss',
})
export class ImportHistoryList {
  readonly portfolioId = input.required<string>();
  /** Only imports into this account, when set. */
  readonly accountId = input<string | null>(null);
  /** Changing this reloads the list (e.g. after the parent imported a file). */
  readonly reloadKey = input(0);
  /** Emitted after an import was undone, so the parent can refresh what it shows. */
  readonly undone = output<ImportUndoSummary>();

  private readonly api = inject(PortfolioApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly confirmHeading = viewChild<ElementRef<HTMLElement>>('confirmHeading');

  protected readonly status = signal<LoadStatus>('loading');
  private readonly history = signal<ImportHistory | null>(null);
  protected readonly pending = signal<PendingUndo | null>(null);
  protected readonly undoing = signal(false);
  protected readonly notice = signal<string | null>(null);

  protected readonly imports = computed(() => {
    const all = this.history()?.imports ?? [];
    const accountId = this.accountId();
    return accountId ? all.filter((i) => i.accounts.some((a) => a.accountId === accountId)) : all;
  });
  protected readonly beforeHistory = computed(() => this.history()?.transactionsImportedBeforeHistory ?? 0);
  protected readonly previewLines = computed(() => {
    const preview = this.pending()?.preview;
    return preview ? undoPreviewLines(preview) : [];
  });
  protected readonly formatImportedAt = formatImportedAt;

  protected fileUrl(item: ImportBatchItem): string {
    return this.api.importFileUrl(this.portfolioId(), item.importBatchId);
  }

  private readonly reloads = signal(0);

  constructor() {
    const request = computed(() => ({ portfolioId: this.portfolioId(), key: this.reloadKey(), n: this.reloads() }));
    toObservable(request)
      .pipe(
        switchMap(({ portfolioId }) => {
          this.status.set('loading');
          return this.api.getImports(portfolioId).pipe(catchError(() => of(null)));
        }),
        takeUntilDestroyed(),
      )
      .subscribe((history) => {
        this.history.set(history);
        this.status.set(history ? 'ready' : 'error');
      });
  }

  /** "Brokerage: 12 added, 3 updated" — for one account, or every account the file touched. */
  protected accountSummary(account: ImportBatchAccount): string {
    const parts = [`${account.inserted} added`];
    if (account.updated > 0) parts.push(`${account.updated} updated`);
    if (account.snapshotsInserted > 0) parts.push(`${account.snapshotsInserted} positions`);
    if (account.failed > 0) parts.push(`${account.failed} failed`);
    return `${account.accountName ?? 'Removed account'}: ${parts.join(', ')}`;
  }

  protected visibleAccounts(item: ImportBatchItem): ImportBatchAccount[] {
    const accountId = this.accountId();
    return accountId ? item.accounts.filter((a) => a.accountId === accountId) : item.accounts;
  }

  /** Opens the confirmation for an undo and loads what it would do. */
  protected startUndo(batch: ImportBatchItem): void {
    this.notice.set(null);
    this.pending.set({ batch, preview: null, error: null });
    afterNextRender(() => this.confirmHeading()?.nativeElement.focus(), { injector: this.injector });

    this.api
      .getImportUndoPreview(this.portfolioId(), batch.importBatchId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (preview) => this.pending.update((p) => (p ? { ...p, preview } : p)),
        error: (err: HttpErrorResponse) => this.pending.update((p) => (p ? { ...p, error: undoErrorMessage(err) } : p)),
      });
  }

  protected cancelUndo(): void {
    this.pending.set(null);
  }

  protected confirmUndo(): void {
    const pending = this.pending();
    if (!pending?.preview || this.undoing()) {
      return;
    }
    this.undoing.set(true);
    this.api
      .undoImport(this.portfolioId(), pending.batch.importBatchId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (summary) => {
          this.undoing.set(false);
          this.pending.set(null);
          this.notice.set(`Undid the import of ${summary.fileName}.`);
          this.reloads.update((n) => n + 1);
          this.undone.emit(summary);
        },
        error: (err: HttpErrorResponse) => {
          this.undoing.set(false);
          this.pending.update((p) => (p ? { ...p, error: undoErrorMessage(err) } : p));
          if (err.status === 409) {
            this.reloads.update((n) => n + 1);
          }
        },
      });
  }
}

function undoErrorMessage(err: HttpErrorResponse): string {
  switch (err.status) {
    case 409:
      return 'This import has already been undone.';
    case 404:
      return 'This import no longer exists.';
    default:
      return "The undo couldn't be completed. Nothing was changed; please try again.";
  }
}
