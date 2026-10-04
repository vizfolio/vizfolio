import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { switchMap } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { ImportUndoSummary } from '../../../core/api/models/imports.models';
import { undoPreviewLines } from '../import-text';

/**
 * "Undo the import of X?" — loads the server's preview of exactly what undoing removes or restores, then undoes on
 * confirmation. Focus moves to the question when it opens. Used by the import history and the post-import summary.
 */
@Component({
  selector: 'app-undo-import-confirm',
  templateUrl: './undo-import-confirm.html',
  styleUrl: './undo-import-confirm.scss',
})
export class UndoImportConfirm {
  readonly portfolioId = input.required<string>();
  readonly importBatchId = input.required<string>();
  readonly fileName = input.required<string>();

  /** The import was undone. */
  readonly undone = output<ImportUndoSummary>();
  /** The user kept the import. */
  readonly cancelled = output<void>();

  private readonly api = inject(PortfolioApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  protected readonly preview = signal<ImportUndoSummary | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly undoing = signal(false);
  protected readonly lines = computed(() => {
    const preview = this.preview();
    return preview ? undoPreviewLines(preview) : [];
  });

  constructor() {
    afterNextRender(() => this.heading()?.nativeElement.focus());

    const request = computed(() => ({ portfolioId: this.portfolioId(), batchId: this.importBatchId() }));
    toObservable(request)
      .pipe(
        switchMap(({ portfolioId, batchId }) => {
          this.preview.set(null);
          this.error.set(null);
          return this.api.getImportUndoPreview(portfolioId, batchId);
        }),
        takeUntilDestroyed(),
      )
      .subscribe({
        next: (preview) => this.preview.set(preview),
        error: (err: HttpErrorResponse) => this.error.set(undoErrorMessage(err)),
      });
  }

  protected confirm(): void {
    if (!this.preview() || this.undoing()) {
      return;
    }
    this.undoing.set(true);
    this.api
      .undoImport(this.portfolioId(), this.importBatchId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (summary) => {
          this.undoing.set(false);
          this.undone.emit(summary);
        },
        error: (err: HttpErrorResponse) => {
          this.undoing.set(false);
          this.error.set(undoErrorMessage(err));
        },
      });
  }

  protected cancel(): void {
    this.cancelled.emit();
  }
}

export function undoErrorMessage(err: HttpErrorResponse): string {
  switch (err.status) {
    case 409:
      return 'This import has already been undone.';
    case 404:
      return 'This import no longer exists.';
    default:
      return "The undo couldn't be completed. Nothing was changed; please try again.";
  }
}
