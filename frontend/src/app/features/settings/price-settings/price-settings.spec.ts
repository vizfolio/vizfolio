import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PriceProvider, PriceStatus } from '../../../core/api/models/prices.models';
import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { PriceSettings } from './price-settings';

const TIINGO: PriceProvider = {
  provider: 'Tiingo', displayName: 'Tiingo', priority: 30, requiresApiKey: true, available: false,
  providesRawCloses: true, keySource: 'None',
};
const STOOQ: PriceProvider = {
  provider: 'Stooq', displayName: 'Stooq (adjusted closes only)', priority: 0, requiresApiKey: false, available: false,
  providesRawCloses: false, keySource: 'None',
};

function status(overrides: Partial<PriceStatus> = {}): PriceStatus {
  return {
    refresh: {
      running: false, pending: false, lastStartedAt: null, lastFinishedAt: '2026-10-03T20:05:00Z', lastTrigger: 'Schedule',
      lastUpserted: 12, lastFailed: 1, lastError: null,
    },
    providersAvailable: 1,
    series: [
      { symbol: 'ZZZZ', kind: 'Symbol', lastAttemptAt: null, lastSource: null, lastOutcome: 'Empty', message: 'No data from Tiingo.',
        neededFrom: null, firstStored: null, lastStored: null, noDataBefore: null },
      { symbol: 'ZXTE', kind: 'Security', lastAttemptAt: null, lastSource: 'Tiingo', lastOutcome: 'Ok', message: null,
        neededFrom: null, firstStored: null, lastStored: null, noDataBefore: null },
    ],
    ...overrides,
  };
}

class MockApi {
  providers = [TIINGO, STOOQ];
  status: PriceStatus = status();
  saved: { provider: string; key: string | null } | null = null;
  saveResult: Observable<PriceProvider> | null = null;
  refreshed = 0;
  getPriceProviders() {
    return of(this.providers);
  }
  getPriceStatus() {
    return of(this.status);
  }
  refreshPrices() {
    this.refreshed++;
    return of(this.status.refresh);
  }
  setPriceProviderKey(provider: string, key: string | null) {
    this.saved = { provider, key };
    return this.saveResult ?? of({ ...TIINGO, available: !!key, keySource: key ? 'Settings' : 'None' } as PriceProvider);
  }
}

function setup(api: MockApi) {
  TestBed.configureTestingModule({ providers: [{ provide: PortfolioApiService, useValue: api }] });
  const fixture = TestBed.createComponent(PriceSettings);
  fixture.detectChanges();
  return { fixture, cmp: fixture.componentInstance as any, el: fixture.nativeElement as HTMLElement };
}

describe('PriceSettings', () => {
  it('lists providers with their state and the series that need a look', () => {
    const { el } = setup(new MockApi());

    const providers = el.querySelectorAll('.provider');
    expect(providers.length).toBe(2);
    expect(providers[0].textContent).toContain('Needs an API key');
    expect(providers[1].textContent).toContain("can't value your holdings");
    expect(providers[1].querySelector('input')).toBeNull();
    expect(el.querySelector('.problems summary')?.textContent).toContain('1 of 2 price series');
    expect(el.querySelector('.problems')?.textContent).toContain('ZZZZ — No data from any provider');
    expect(el.textContent).toContain('12 new close(s)');
  });

  it('points to getting a key when no provider is set up', () => {
    const api = new MockApi();
    api.status = status({ providersAvailable: 0 });
    const { el } = setup(api);

    expect(el.querySelector('.callout')?.textContent).toContain('tiingo.com');
  });

  it('saves a typed key, clears the field and marks the provider active', () => {
    const api = new MockApi();
    const { fixture, cmp, el } = setup(api);

    cmp.onDraft('Tiingo', { target: { value: ' my-key ' } });
    cmp.saveKey(TIINGO);
    fixture.detectChanges();

    expect(api.saved).toEqual({ provider: 'Tiingo', key: 'my-key' });
    expect(cmp.drafts()['Tiingo']).toBe('');
    expect(el.querySelector('.provider')?.textContent).toContain('Active');
    expect(el.querySelector('.provider')?.textContent).toContain('Remove key');
  });

  it('explains a key that the server configuration already sets', () => {
    const api = new MockApi();
    api.saveResult = throwError(() => new HttpErrorResponse({ status: 409 }));
    const { fixture, cmp, el } = setup(api);

    cmp.onDraft('Tiingo', { target: { value: 'k' } });
    cmp.saveKey(TIINGO);
    fixture.detectChanges();

    expect(el.querySelector('.provider [role="alert"]')?.textContent).toContain('server configuration');
  });

  it('queues a fetch of every price on request', () => {
    const api = new MockApi();
    const { cmp } = setup(api);

    cmp.refreshNow();

    expect(api.refreshed).toBe(1);
  });
});
