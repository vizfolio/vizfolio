import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  DataHealthReport,
  HealthActionKind,
  HealthFinding,
  ImpliedContributionPreview,
} from '../../../core/api/models/health.models';
import { formatMoney } from '../../../shared/util/performance-format';

/** Actions the page hosting the list carries out (switching to another tab). */
export type HealthNavigation = Extract<HealthActionKind, 'AdjustStartingPosition' | 'ReimportFile'>;

/** The by-year breakdown of an account's implied contributions, once loaded. */
interface ImpliedReview {
  status: 'loading' | 'ready' | 'error';
  preview: ImpliedContributionPreview | null;
}

/**
 * Data health findings, worst first, each in plain language with what to do about it: fetch prices (queued at once),
 * add a price provider (Settings), adjust starting positions or import a file (the host switches tab), or review the
 * implied contributions (their by-year breakdown, loaded on demand). With `accountNames`, findings are labelled by
 * account (portfolio scope).
 */
@Component({
  selector: 'app-health-list',
  imports: [NgTemplateOutlet, RouterLink],
  templateUrl: './health-list.html',
  styleUrl: './health-list.scss',
})
export class HealthList {
  readonly portfolioId = input.required<string>();
  readonly report = input.required<DataHealthReport>();
  /** Account names by id, to label each finding (leave empty on an account page). */
  readonly accountNames = input<Record<string, string>>({});

  /** The user picked an action the host page carries out. */
  readonly navigate = output<{ kind: HealthNavigation; finding: HealthFinding }>();
  /** A price fetch was queued: the host re-checks health shortly. */
  readonly pricesRequested = output<void>();

  private readonly api = inject(PortfolioApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly blocking = computed(() => this.report().findings.filter((f) => f.severity === 'Blocking'));
  protected readonly info = computed(() => this.report().findings.filter((f) => f.severity === 'Info'));
  protected readonly fetchQueued = signal(false);
  protected readonly implied = signal<Record<string, ImpliedReview>>({});
  protected readonly formatMoney = formatMoney;

  protected accountLabel(finding: HealthFinding): string | null {
    return finding.accountId ? (this.accountNames()[finding.accountId] ?? null) : null;
  }

  protected act(finding: HealthFinding): void {
    switch (finding.action.kind) {
      case 'FetchPrices':
        this.fetchPrices();
        break;
      case 'ReviewImpliedContributions':
        this.toggleImplied(finding);
        break;
      case 'AdjustStartingPosition':
      case 'ReimportFile':
        this.navigate.emit({ kind: finding.action.kind, finding });
        break;
    }
  }

  protected impliedFor(finding: HealthFinding): ImpliedReview | null {
    return finding.accountId ? (this.implied()[finding.accountId] ?? null) : null;
  }

  private fetchPrices(): void {
    if (this.fetchQueued()) {
      return;
    }
    this.fetchQueued.set(true);
    this.api
      .refreshPrices()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.pricesRequested.emit(),
        error: () => this.fetchQueued.set(false),
      });
  }

  private toggleImplied(finding: HealthFinding): void {
    const accountId = finding.accountId;
    if (!accountId) {
      return;
    }
    if (this.implied()[accountId]) {
      this.implied.update(({ [accountId]: _, ...rest }) => rest);
      return;
    }
    this.implied.update((all) => ({ ...all, [accountId]: { status: 'loading', preview: null } }));
    this.api
      .getImpliedContributions(this.portfolioId(), accountId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (preview) => this.implied.update((all) => ({ ...all, [accountId]: { status: 'ready', preview } })),
        error: () => this.implied.update((all) => ({ ...all, [accountId]: { status: 'error', preview: null } })),
      });
  }
}
