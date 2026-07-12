import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import { PortfolioSummary } from '../../core/api/models/performance.models';
import { Portfolios } from './portfolios';

function portfolio(id: string, name: string, accounts = 0): PortfolioSummary {
  return { portfolioId: id, name, createdAt: '2026-01-01T00:00:00Z', accountCount: accounts };
}

class MockApi {
  list: Observable<PortfolioSummary[]> = of([portfolio('p1', 'Retirement', 2)]);
  created: Observable<PortfolioSummary> = of(portfolio('p2', 'Taxable', 0));
  getPortfolios() {
    return this.list;
  }
  createPortfolio(name: string) {
    this.createdWith = name;
    return this.created;
  }
  createdWith: string | null = null;
}

function setup(api: MockApi): {
  fixture: ComponentFixture<Portfolios>;
  cmp: any;
  navigate: ReturnType<typeof vi.fn>;
} {
  TestBed.configureTestingModule({
    imports: [Portfolios],
    providers: [
      { provide: PortfolioApiService, useValue: api },
      provideRouter([]),
    ],
  });
  const navigate = vi.fn().mockResolvedValue(true);
  const router = TestBed.inject(Router);
  vi.spyOn(router, 'navigate').mockImplementation(navigate as never);

  const fixture = TestBed.createComponent(Portfolios);
  fixture.detectChanges();
  return { fixture, cmp: fixture.componentInstance as any, navigate };
}

describe('Portfolios', () => {
  beforeEach(() => localStorage.clear());

  it('lists the portfolios owned by the active-portfolio service', () => {
    const { fixture } = setup(new MockApi());
    const names = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.portfolio-name'),
    ).map((el) => el.textContent);
    expect(names.length).toBe(1);
    expect(names[0]).toContain('Retirement');
  });

  it('marks the active portfolio with a badge', () => {
    const { fixture } = setup(new MockApi());
    const badge = (fixture.nativeElement as HTMLElement).querySelector('.active-badge');
    expect(badge?.textContent?.trim()).toBe('Active');
  });

  it('creates a portfolio, adopts it as active, and resets the form', () => {
    const api = new MockApi();
    const { cmp, fixture } = setup(api);

    cmp.name.set('Taxable');
    cmp.create();
    fixture.detectChanges();

    expect(api.createdWith).toBe('Taxable');
    expect(cmp.name()).toBe('');
    expect(cmp.activeId()).toBe('p2');
    expect(cmp.portfolios().map((p: PortfolioSummary) => p.portfolioId)).toContain('p2');
  });

  it('does not submit a blank name', () => {
    const api = new MockApi();
    const { cmp } = setup(api);
    cmp.name.set('   ');
    cmp.create();
    expect(api.createdWith).toBeNull();
  });

  it('surfaces an error when creation fails', () => {
    const api = new MockApi();
    api.created = throwError(() => new Error('boom'));
    const { cmp } = setup(api);
    cmp.name.set('Oops');
    cmp.create();
    expect(cmp.error()).toContain('Could not create');
    expect(cmp.submitting()).toBe(false);
  });

  it('selects a portfolio and navigates to the dashboard on open', () => {
    const { cmp, navigate } = setup(new MockApi());
    cmp.open('p1');
    expect(cmp.activeId()).toBe('p1');
    expect(navigate).toHaveBeenCalledWith(['/dashboard']);
  });
});
