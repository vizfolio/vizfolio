import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';

import { PriceFetchOutcome, PriceProvider, PriceSeriesStatus, PriceStatus } from '../../../core/api/models/prices.models';
import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { pollWhilePending } from '../../../shared/util/poll';

const OUTCOME_TEXT: Record<PriceFetchOutcome, string> = {
  Ok: 'Up to date',
  Empty: 'No data from any provider',
  Failed: 'Fetch failed',
  NoSource: 'No provider set up',
  AdjustedOnly: 'Only adjusted prices available',
};

const DATE_TIME = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' });

/**
 * Settings → Prices: which providers are set up (and an API key field for those that need one), whether prices are
 * being fetched, and any symbol whose prices couldn't be fetched. Prices are otherwise fetched automatically — after
 * each import, at startup and daily after the US close — so this is where a user adds a key or sees what's missing.
 */
@Component({
  selector: 'app-price-settings',
  templateUrl: './price-settings.html',
  styleUrl: './price-settings.scss',
})
export class PriceSettings {
  private readonly api = inject(PortfolioApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly providers = signal<PriceProvider[]>([]);
  protected readonly status = signal<PriceStatus | null>(null);
  protected readonly loadError = signal<string | null>(null);

  /** Keys typed but not yet saved, by provider. */
  protected readonly drafts = signal<Record<string, string>>({});
  protected readonly saving = signal<string | null>(null);
  protected readonly keyMessage = signal<{ provider: string; text: string; error: boolean } | null>(null);

  protected readonly updating = computed(() => {
    const refresh = this.status()?.refresh;
    return !!refresh && (refresh.running || refresh.pending);
  });
  protected readonly noProvider = computed(() => this.status()?.providersAvailable === 0);
  protected readonly problems = computed(() =>
    (this.status()?.series ?? []).filter((s) => s.lastOutcome !== 'Ok' || s.message),
  );
  protected readonly seriesCount = computed(() => this.status()?.series.length ?? 0);

  constructor() {
    this.loadProviders();
    this.watchStatus();
  }

  protected outcomeText(series: PriceSeriesStatus): string {
    return OUTCOME_TEXT[series.lastOutcome] ?? series.lastOutcome;
  }

  protected formatWhen(iso: string | null): string {
    return iso ? DATE_TIME.format(new Date(iso)) : '';
  }

  protected providerState(provider: PriceProvider): string {
    if (!provider.providesRawCloses) return provider.available ? 'On (adjusted prices only)' : 'Off';
    if (provider.available) return 'Active';
    return provider.requiresApiKey ? 'Needs an API key' : 'Off';
  }

  protected onDraft(provider: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.drafts.update((d) => ({ ...d, [provider]: value }));
  }

  protected saveKey(provider: PriceProvider): void {
    const key = (this.drafts()[provider.provider] ?? '').trim();
    if (!key) return;
    this.storeKey(provider, key, 'Saved. Fetching prices…');
  }

  protected removeKey(provider: PriceProvider): void {
    this.storeKey(provider, null, 'Key removed.');
  }

  /** Queues a fetch for every holding, then follows its progress. */
  protected refreshNow(): void {
    this.api
      .refreshPrices()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.watchStatus(),
        error: () => this.loadError.set("Couldn't start a price refresh. Please try again."),
      });
  }

  private storeKey(provider: PriceProvider, key: string | null, done: string): void {
    this.saving.set(provider.provider);
    this.keyMessage.set(null);
    this.api
      .setPriceProviderKey(provider.provider, key)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.providers.update((list) => list.map((p) => (p.provider === updated.provider ? updated : p)));
          this.drafts.update((d) => ({ ...d, [provider.provider]: '' }));
          this.saving.set(null);
          this.keyMessage.set({ provider: provider.provider, text: done, error: false });
          if (key) this.watchStatus();
        },
        error: (err: HttpErrorResponse) => {
          this.saving.set(null);
          this.keyMessage.set({
            provider: provider.provider,
            text: err.status === 409
              ? 'This key is set in the server configuration, which takes precedence.'
              : "Couldn't save the key. Please try again.",
            error: true,
          });
        },
      });
  }

  private loadProviders(): void {
    this.api
      .getPriceProviders()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (providers) => this.providers.set(providers),
        error: () => this.loadError.set("Couldn't load the price providers."),
      });
  }

  /** Loads the status, and keeps reloading it while a refresh is running or queued. */
  private watchStatus(): void {
    of(null)
      .pipe(
        switchMap(() =>
          pollWhilePending(
            () => this.api.getPriceStatus(),
            (s) => s.refresh.running || s.refresh.pending,
            3000,
          ),
        ),
        catchError(() => {
          this.loadError.set("Couldn't load the price status.");
          return of(null);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((status) => {
        if (status) this.status.set(status);
      });
  }
}
