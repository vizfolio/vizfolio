import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import { ActivePortfolioService } from '../../core/portfolio/active-portfolio.service';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';

/**
 * Portfolios management: list existing portfolios, create new ones, and pick the active one.
 * The list and active-selection are owned by ActivePortfolioService (shared with the topbar
 * switcher); this page just drives it.
 */
@Component({
  selector: 'app-portfolios',
  imports: [EmptyState],
  templateUrl: './portfolios.html',
  styleUrl: './portfolios.scss',
})
export class Portfolios {
  private readonly api = inject(PortfolioApiService);
  private readonly activePortfolio = inject(ActivePortfolioService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly portfolios = this.activePortfolio.portfolios;
  protected readonly activeId = this.activePortfolio.activeId;

  protected readonly name = signal('');
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly canCreate = computed(
    () => this.name().trim().length > 0 && !this.submitting(),
  );

  protected onNameInput(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  /** Create a portfolio, then adopt it as the active one and reset the form. */
  protected create(): void {
    const name = this.name().trim();
    if (!name || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    this.api
      .createPortfolio(name)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.activePortfolio.adopt(created);
          this.name.set('');
          this.submitting.set(false);
        },
        error: () => {
          this.error.set('Could not create the portfolio. Please try again.');
          this.submitting.set(false);
        },
      });
  }

  /** Make a portfolio active and jump to its dashboard. */
  protected open(portfolioId: string): void {
    this.activePortfolio.select(portfolioId);
    void this.router.navigate(['/dashboard']);
  }

  protected accountsLabel(count: number): string {
    return `${count} ${count === 1 ? 'account' : 'accounts'}`;
  }

  protected formatDate(iso: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime())
      ? iso
      : date.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
