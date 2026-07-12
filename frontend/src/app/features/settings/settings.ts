import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import {
  ImportAllResponse,
  ImportResult,
  RelinkLedgerResponse,
} from '../../core/api/models/admin.models';
import { ThemeToggle } from '../../layout/theme-toggle/theme-toggle';

/** State of a one-shot admin action. */
export interface ActionState<T> {
  status: 'idle' | 'running' | 'done' | 'error';
  result?: T;
  error?: string;
}

const IDLE: ActionState<never> = { status: 'idle' };

/**
 * Settings / Admin: trigger the reference-data extract imports and the ledger re-link, and
 * house app appearance controls. Import endpoints are guarded by a run-gate server-side, so a
 * 409 means another import is already running.
 */
@Component({
  selector: 'app-settings',
  imports: [ThemeToggle],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings {
  private readonly api = inject(PortfolioApiService);
  private readonly destroyRef = inject(DestroyRef);

  // Form inputs.
  protected readonly secTickers = signal('');
  protected readonly secForce = signal(false);
  protected readonly fundSeries = signal('');
  protected readonly fundForce = signal(false);
  protected readonly allForce = signal(false);

  // Per-action state.
  protected readonly securities = signal<ActionState<ImportResult>>(IDLE);
  protected readonly funds = signal<ActionState<ImportResult>>(IDLE);
  protected readonly all = signal<ActionState<ImportAllResponse>>(IDLE);
  protected readonly relink = signal<ActionState<RelinkLedgerResponse>>(IDLE);

  protected onText(target: (v: string) => void, event: Event): void {
    target((event.target as HTMLInputElement).value);
  }

  protected onCheck(target: (v: boolean) => void, event: Event): void {
    target((event.target as HTMLInputElement).checked);
  }

  protected runSecurities(): void {
    this.run(this.securities, () =>
      this.api.importSecurities({
        tickers: parseList(this.secTickers()),
        force: this.secForce(),
      }),
    );
  }

  protected runFunds(): void {
    this.run(this.funds, () =>
      this.api.importFunds({
        seriesIds: parseList(this.fundSeries()),
        force: this.fundForce(),
      }),
    );
  }

  protected runAll(): void {
    this.run(this.all, () => this.api.importAll({ force: this.allForce() }));
  }

  protected runRelink(): void {
    this.run(this.relink, () => this.api.relinkLedger());
  }

  /** Runs an admin action, threading its lifecycle into the given state signal. */
  private run<T>(
    state: ReturnType<typeof signal<ActionState<T>>>,
    call: () => Observable<T>,
  ): void {
    if (state().status === 'running') {
      return;
    }
    state.set({ status: 'running' });
    call()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => state.set({ status: 'done', result }),
        error: (err: HttpErrorResponse) =>
          state.set({
            status: 'error',
            error:
              err.status === 409
                ? 'Another extracts import is already in progress. Try again once it finishes.'
                : 'The operation failed. Please try again.',
          }),
      });
  }
}

/** Splits a comma-separated input into a trimmed list, or undefined when empty. */
function parseList(value: string): string[] | undefined {
  const items = value
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
  return items.length > 0 ? items : undefined;
}
