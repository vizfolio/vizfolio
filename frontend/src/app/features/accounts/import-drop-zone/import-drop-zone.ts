import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  AccountSelection,
  ImportParser,
  PortfolioImportResult,
  PortfolioImportStatus,
  StatementAssignment,
} from '../../../core/api/models/imports.models';
import { AccountSummary } from '../../../core/api/models/performance.models';
import { FileUpload } from '../../../shared/ui/file-upload/file-upload';
import { SelectField } from '../../../shared/ui/select-field/select-field';
import { AccountPicker } from '../account-picker/account-picker';
import { parserAcceptAttr, parserFormatOptions } from '../import-format-options';
import { portfolioImportErrorMessage } from '../import-text';
import { ImportSummary } from '../import-summary/import-summary';

type DropState = 'queued' | 'importing' | 'asking' | 'done' | 'skipped' | 'error';

/** One dropped file and how its import is going. */
interface DropItem {
  id: number;
  file: File;
  state: DropState;
  result: PortfolioImportResult | null;
  error: string | null;
  /** What the import asked, and the answers given so far (sent with the file again once all are in). */
  selections: AccountSelection[];
  answers: StatementAssignment[];
}

/**
 * The one place to import broker files: drop any number of supported files and each is imported in turn into the
 * account it belongs to — by account number, or for a file without one (e.g. the Vanguard report) by the
 * transactions already in an account. When that isn't clear the file waits for an inline answer (the queue pauses so
 * later files see it). Each finished file shows its summary, with an undo. The format is detected automatically; the
 * override sits under "Advanced".
 */
@Component({
  selector: 'app-import-drop-zone',
  imports: [AccountPicker, FileUpload, ImportSummary, SelectField],
  templateUrl: './import-drop-zone.html',
  styleUrl: './import-drop-zone.scss',
})
export class ImportDropZone {
  readonly portfolioId = input.required<string>();
  /** The portfolio's accounts, to name them in results. */
  readonly accounts = input<AccountSummary[]>([]);
  readonly label = input('Drag broker files here, or choose them');

  /** A file was imported or undone: the parent refreshes its accounts and history. */
  readonly changed = output<void>();

  private readonly api = inject(PortfolioApiService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly parsers = signal<ImportParser[]>([]);
  protected readonly sourceSystem = signal('');
  protected readonly formatOptions = computed(() => parserFormatOptions(this.parsers()));
  protected readonly acceptAttr = computed(() => parserAcceptAttr(this.parsers()));

  protected readonly items = signal<DropItem[]>([]);
  protected readonly busy = computed(() => this.items().some((i) => i.state === 'importing'));
  private nextId = 0;

  constructor() {
    this.api
      .getImportParsers()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((parsers) => this.parsers.set(parsers));
  }

  protected onFiles(files: File[]): void {
    const added = files.map<DropItem>((file) => ({
      id: this.nextId++,
      file,
      state: 'queued',
      result: null,
      error: null,
      selections: [],
      answers: [],
    }));
    this.items.update((items) => [...added.reverse(), ...items.filter((i) => i.state !== 'skipped')]);
    this.processNext();
  }

  /** Statements of an asking file that still need an answer. */
  protected unanswered(item: DropItem): AccountSelection[] {
    const answered = new Set(item.answers.map((a) => a.fileAccountNumber));
    return item.selections.filter((s) => !answered.has(s.fileAccountNumber));
  }

  protected onChosen(item: DropItem, answer: StatementAssignment): void {
    const answers = [...item.answers.filter((a) => a.fileAccountNumber !== answer.fileAccountNumber), answer];
    const complete = item.selections.every((s) => answers.some((a) => a.fileAccountNumber === s.fileAccountNumber));
    this.update(item.id, { answers, state: complete ? 'queued' : 'asking' });
    if (complete) {
      this.processNext();
    }
  }

  protected onSkipped(item: DropItem): void {
    this.update(item.id, { state: 'skipped' });
    this.processNext();
  }

  protected onUndone(): void {
    this.changed.emit();
  }

  protected dismiss(item: DropItem): void {
    this.items.update((items) => items.filter((i) => i.id !== item.id));
  }

  /** Imports the oldest queued file, unless one is importing or waiting for an answer (answers can affect later files). */
  private processNext(): void {
    const items = this.items();
    if (items.some((i) => i.state === 'importing' || i.state === 'asking')) {
      return;
    }
    const next = [...items].reverse().find((i) => i.state === 'queued');
    if (!next) {
      return;
    }

    this.update(next.id, { state: 'importing', error: null });
    this.api
      .importPortfolioFile(this.portfolioId(), next.file, this.sourceSystem() || undefined, next.answers)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          if (result.status === PortfolioImportStatus.NeedsAccountSelection) {
            this.update(next.id, { state: 'asking', selections: result.selections, answers: [] });
            return;
          }
          this.update(next.id, { state: 'done', result });
          this.changed.emit();
          this.processNext();
        },
        error: (err: HttpErrorResponse) => {
          this.update(next.id, { state: 'error', error: portfolioImportErrorMessage(err) });
          this.processNext();
        },
      });
  }

  private update(id: number, patch: Partial<DropItem>): void {
    this.items.update((items) => items.map((i) => (i.id === id ? { ...i, ...patch } : i)));
  }
}
