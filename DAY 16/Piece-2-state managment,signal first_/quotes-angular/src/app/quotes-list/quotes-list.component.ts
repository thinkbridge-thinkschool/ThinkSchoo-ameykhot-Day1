import { Component, computed, effect, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../auth.service';
import { StarService } from '../star.service';
import { QuotesStore } from '../stores/quotes.store';

@Component({
  selector: 'app-quotes-list',
  standalone: true,
  imports: [FormsModule, DecimalPipe],
  templateUrl: './quotes-list.component.html',
  styleUrl: './quotes-list.component.css',
})
export class QuotesListComponent {
  private readonly store = inject(QuotesStore);
  private readonly router = inject(Router);
  readonly stars = inject(StarService);
  readonly auth  = inject(AuthService);

  // Delegate data state to the store — template keeps the same signal names
  readonly quotes        = this.store.quotes;
  readonly isListLoading = this.store.isLoading;
  readonly listError     = this.store.error;
  readonly currentPage   = this.store.currentPage;
  readonly totalQuotes   = this.store.total;

  // Component-owned UI signals
  searchTerm = signal('');
  activeTab  = signal<'all' | 'starred'>('all');

  private lastSearch = '';
  readonly pageSize  = 10;

  totalPages = computed(() => Math.max(1, Math.ceil(this.totalQuotes() / this.pageSize)));
  isLastPage = computed(() => this.currentPage() >= this.totalPages());

  constructor() {
    effect(() => {
      const page   = this.currentPage();
      const search = this.searchTerm();

      if (search !== this.lastSearch) {
        this.lastSearch = search;
        if (page !== 1) {
          this.store.setPage(1);
          return;
        }
      }

      this.store.loadQuotes(search);
    });
  }

  selectQuote(id: number): void   { this.router.navigate(['/quotes', id]); }
  goToCreate(): void               { this.router.navigate(['/create-quote']); }
  logout(): void                   { this.auth.logout(); this.router.navigate(['/login']); }
  setTab(tab: 'all' | 'starred'): void { this.activeTab.set(tab); }

  shuffle(): void {
    const max = this.totalPages();
    if (max > 1) this.store.setPage(Math.floor(Math.random() * max) + 1);
  }

  retry(): void    { this.store.loadQuotes(this.searchTerm()); }
  prevPage(): void { if (this.currentPage() > 1) this.store.setPage(this.currentPage() - 1); }
  nextPage(): void { if (!this.isLastPage()) this.store.setPage(this.currentPage() + 1); }
}
