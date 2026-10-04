import { TestBed } from '@angular/core/testing';
import { Observable, of } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { ImportUndoSummary, PortfolioImportResult, PortfolioImportStatus } from '../../../core/api/models/imports.models';
import { PortfolioPerformance } from '../../../core/api/models/performance.models';
import { accountResult, importResult } from '../testing/import-fixtures';
import { ImportSummary } from './import-summary';

function performance(cause: string | null): PortfolioPerformance {
  return {
    endingBalance: { value: 1234.5, isComplete: cause === null, missing: cause ? [{ cause }] : [] },
    startingBalance: { value: 0, isComplete: true, missing: [] },
    returns: { moneyWeighted: { rate: 0.12, basis: 'Annualized', annualizedRate: null, fallbackReason: null, reason: null } },
    currencyCode: 'EUR',
  } as unknown as PortfolioPerformance;
}

const UNDO: ImportUndoSummary = {
  importBatchId: 'b1', fileName: 'ira.xlsx', transactions: 3, snapshots: 0, updatesReverted: 0,
  holdingsRemoved: 0, accountsRemoved: 0, laterImportsReplayed: [], laterImportsWithoutFile: [],
};

class MockApi {
  perf: Observable<PortfolioPerformance> = of(performance(null));
  undone: string | null = null;
  getAccountPerformance() {
    return this.perf;
  }
  getImportUndoPreview() {
    return of(UNDO);
  }
  undoImport(_pid: string, batchId: string) {
    this.undone = batchId;
    return of(UNDO);
  }
}

function setup(api: MockApi, result: PortfolioImportResult) {
  TestBed.configureTestingModule({ providers: [{ provide: PortfolioApiService, useValue: api }] });
  const fixture = TestBed.createComponent(ImportSummary);
  fixture.componentRef.setInput('portfolioId', 'p1');
  fixture.componentRef.setInput('result', result);
  fixture.componentRef.setInput('fileName', 'ira.xlsx');
  fixture.componentRef.setInput('accounts', [
    { accountId: 'a1', portfolioId: 'p1', name: 'Rollover IRA', institutionCode: 'vanguard.com', accountNumber: '1234', accountType: null, createdAt: '', transactionCount: 0 },
  ]);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

describe('ImportSummary', () => {
  it('says what the file was, where it went and how, what it covered and what it did', () => {
    const { el } = setup(
      new MockApi(),
      importResult({
        parserDisplayName: 'Vanguard transaction report',
        accounts: [
          accountResult({
            routing: { method: 'Fingerprint', matchingRows: 2514 },
            firstDate: '2025-01-02',
            lastDate: '2025-12-31',
            inserted: 12,
            skipped: 2514,
            updated: 2,
          }),
        ],
      }),
    );

    expect(el.querySelector('.heading')?.textContent).toContain('ira.xlsx · Vanguard transaction report');
    expect(el.textContent).toContain('Rollover IRA');
    expect(el.textContent).toContain('Matched by 2,514 transactions already in this account.');
    expect(el.querySelector('.counts')?.textContent?.replace(/\s+/g, ' ')).toContain(
      'Jan 2, 2025 – Dec 31, 2025: 12 added, 2514 already there , 2 updated',
    );
  });

  it('lists why rows failed', () => {
    const { el } = setup(
      new MockApi(),
      importResult({ accounts: [accountResult({ failed: 1, failures: [{ key: 'row[7]', reason: 'Unreadable date' }] })] }),
    );

    expect(el.querySelector('.failures')?.textContent).toContain('row[7]: Unreadable date');
  });

  it("shows the account's value and Your return once prices are in, and implied contributions in its currency", () => {
    const { el } = setup(
      new MockApi(),
      importResult({ accounts: [accountResult({ impliedContributions: 2, impliedContributionsAmount: 500 })] }),
    );

    expect(el.querySelector('.headline')?.textContent).toContain('Value €1,234.50');
    expect(el.querySelector('.headline')?.textContent).toContain('Your return +12.0%');
    expect(el.textContent).toContain('2 implied contributions (€500.00)');
  });

  it('says prices are being fetched while they download', () => {
    const api = new MockApi();
    api.perf = of(performance('PricesPending'));
    const { el } = setup(api, importResult());

    expect(el.textContent).toContain('Fetching prices…');
  });

  it('undoes the import from the summary after confirming', () => {
    const api = new MockApi();
    const { fixture, el } = setup(api, importResult());
    let undone: ImportUndoSummary | null = null;
    fixture.componentInstance.undone.subscribe((s) => (undone = s));

    (el.querySelector('.undo button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('app-undo-import-confirm')?.textContent).toContain('Removes 3 transactions');
    (el.querySelector('.confirm .btn--danger') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(api.undone).toBe('b1');
    expect(undone).toEqual(UNDO);
    expect(el.textContent).toContain('Undid the import of ira.xlsx.');
  });

  it('shows the earlier result of a re-upload without offering undo or fetching prices', () => {
    const { el } = setup(new MockApi(), importResult({ status: PortfolioImportStatus.AlreadyImported }));

    expect(el.textContent).toContain('already imported on');
    expect(el.querySelector('.undo')).toBeNull();
    expect(el.querySelector('.headline')).toBeNull();
  });
});
