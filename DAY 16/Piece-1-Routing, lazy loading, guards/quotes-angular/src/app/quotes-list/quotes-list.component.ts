import { Component, computed, effect, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../auth.service';
import { Quote } from '../quote.model';
import { QuotesService } from '../quotes.service';
import { StarService } from '../star.service';

@Component({
  selector: 'app-quotes-list',
  standalone: true,
  imports: [FormsModule, DecimalPipe],
  templateUrl: './quotes-list.component.html',
  styleUrl: './quotes-list.component.css',
})
export class QuotesListComponent {
  private service = inject(QuotesService);
  private router  = inject(Router);
  readonly stars  = inject(StarService);
  readonly auth   = inject(AuthService);

  quotes        = signal<Quote[]>([]);
  isListLoading = signal(true);
  listError     = signal<string | null>(null);
  totalQuotes   = signal(0);
  currentPage   = signal(1);
  searchTerm    = signal('');
  activeTab     = signal<'all' | 'starred'>('all');

  private loadTrigger = signal(0);
  private lastSearch  = '';
  readonly pageSize   = 10;

  totalPages = computed(() => Math.max(1, Math.ceil(this.totalQuotes() / this.pageSize)));
  isLastPage = computed(() => this.currentPage() >= this.totalPages());

  constructor() {
    effect(() => {
      const page   = this.currentPage();
      const search = this.searchTerm();
      void this.loadTrigger();

      if (search !== this.lastSearch) {
        this.lastSearch = search;
        if (page !== 1) {
          this.currentPage.set(1);
          return;
        }
      }

      this.isListLoading.set(true);
      this.listError.set(null);

      this.service.getQuotes(page, this.pageSize, search).subscribe({
        next: response => {
          this.quotes.set(response.data);
          this.totalQuotes.set(response.pagination.total);
          this.isListLoading.set(false);
        },
        error: (err: Error) => {
          this.listError.set('Failed to load quotes: ' + (err.message ?? 'Unknown error'));
          this.isListLoading.set(false);
        },
      });
    });
  }

  selectQuote(id: number): void   { this.router.navigate(['/quotes', id]); }
  goToCreate(): void               { this.router.navigate(['/create-quote']); }
  logout(): void                   { this.auth.logout(); this.router.navigate(['/login']); }
  setTab(tab: 'all' | 'starred'): void { this.activeTab.set(tab); }
  shuffle(): void {
    const max = this.totalPages();
    if (max > 1) this.currentPage.set(Math.floor(Math.random() * max) + 1);
  }
  retry(): void      { this.loadTrigger.update(n => n + 1); }
  prevPage(): void   { if (this.currentPage() > 1) this.currentPage.update(p => p - 1); }
  nextPage(): void   { if (!this.isLastPage()) this.currentPage.update(p => p + 1); }
}
