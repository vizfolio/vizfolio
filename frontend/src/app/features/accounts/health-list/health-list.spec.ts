import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { DataHealthReport, HealthFinding } from '../../../core/api/models/health.models';
import { HealthList } from './health-list';

function finding(overrides: Partial<HealthFinding>): HealthFinding {
  return {
    code: 'UnpricedHolding',
    severity: 'Blocking',
    accountId: 'a1',
    holdingId: 'h1',
    symbol: 'ZXFND',
    from: '2025-01-02',
    to: '2025-02-01',
    message: 'No price for ZXFND.',
    action: { kind: 'FetchPrices', label: 'Fetch prices' },
    details: {
      ledgerQuantity: null, brokerQuantity: null, amount: null, count: null, priceFetchOutcome: null,
      priceFetchMessage: null, pricesPending: false, warningCode: null, samples: [], files: [],
    },
    ...overrides,
  };
}

function report(findings: HealthFinding[]): DataHealthReport {
  return { status: findings.length ? 'NeedsAttention' : 'Healthy', currencyCode: 'USD', accounts: [], findings };
}

class MockApi {
  refreshed = 0;
  refreshPrices() {
    this.refreshed++;
    return of({});
  }
  getImpliedContributions() {
    return of({ tolerance: 1, totalAmount: 750, endingCash: 0, byYear: [{ year: 2012, amount: 750, count: 2 }] });
  }
}

function setup(r: DataHealthReport, accountNames: Record<string, string> = {}) {
  const api = new MockApi();
  TestBed.configureTestingModule({ providers: [{ provide: PortfolioApiService, useValue: api }, provideRouter([])] });
  const fixture = TestBed.createComponent(HealthList);
  fixture.componentRef.setInput('portfolioId', 'p1');
  fixture.componentRef.setInput('report', r);
  fixture.componentRef.setInput('accountNames', accountNames);
  fixture.detectChanges();
  return { fixture, api, el: fixture.nativeElement as HTMLElement };
}

describe('HealthList', () => {
  it('says so when everything checks out', () => {
    const { el } = setup(report([]));
    expect(el.textContent).toContain('Everything checks out');
  });

  it('groups findings into what needs attention and what is worth knowing, labelled by account', () => {
    const { el } = setup(
      report([
        finding({}),
        finding({ code: 'PreHistoryPosition', severity: 'Info', message: 'We assumed you held 5 ZXFND.', action: { kind: 'AdjustStartingPosition', label: 'Adjust starting positions' } }),
      ]),
      { a1: 'IRA' },
    );

    const sections = el.querySelectorAll('section');
    expect(sections[0].textContent).toContain('Needs attention');
    expect(sections[0].textContent).toContain('IRA: No price for ZXFND.');
    expect(sections[1].textContent).toContain('Worth knowing');
    expect(sections[1].textContent).toContain('We assumed you held 5 ZXFND.');
  });

  it('queues a price fetch once and tells the host', () => {
    const { fixture, api, el } = setup(report([finding({})]));
    let requested = 0;
    fixture.componentInstance.pricesRequested.subscribe(() => requested++);

    const button = el.querySelector('.finding button') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
    button.click();

    expect(api.refreshed).toBe(1);
    expect(requested).toBe(1);
    expect(button.textContent).toContain('Fetching prices…');
  });

  it('hands actions on other tabs to the host', () => {
    const { fixture, el } = setup(
      report([finding({ severity: 'Info', action: { kind: 'AdjustStartingPosition', label: 'Adjust starting positions' } })]),
    );
    const kinds: string[] = [];
    fixture.componentInstance.navigate.subscribe((e) => kinds.push(e.kind));

    (el.querySelector('.finding button') as HTMLButtonElement).click();

    expect(kinds).toEqual(['AdjustStartingPosition']);
  });

  it('links to Settings when no price provider has a holding', () => {
    const { el } = setup(report([finding({ action: { kind: 'AddPriceProviderKey', label: 'Add a price provider' } })]));
    expect(el.querySelector('.finding a')?.getAttribute('href')).toBe('/settings');
  });

  it('shows implied contributions by year on request', () => {
    const { fixture, el } = setup(
      report([
        finding({
          code: 'ImpliedContributions', severity: 'Info', message: '2 purchases had no recorded deposit.',
          action: { kind: 'ReviewImpliedContributions', label: 'Review' },
        }),
      ]),
    );

    (el.querySelector('.finding button') as HTMLButtonElement).click();
    fixture.detectChanges();

    const cells = [...el.querySelectorAll('table.implied tbody td')].map((td) => td.textContent?.trim());
    expect(cells).toEqual(['2012', '2', '$750.00']);
  });

  it('shows example rows for import warnings', () => {
    const { el } = setup(
      report([finding({ code: 'ImportWarning', severity: 'Info', action: { kind: 'None', label: null }, details: { ...finding({}).details, samples: ['Mystery', 'Oddity'] } })]),
    );
    expect(el.querySelector('.samples')?.textContent).toContain('For example: Mystery, Oddity');
    expect(el.querySelector('.finding button')).toBeNull();
  });
});
