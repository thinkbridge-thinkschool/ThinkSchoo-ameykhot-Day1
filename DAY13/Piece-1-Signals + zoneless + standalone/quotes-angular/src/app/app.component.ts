import {
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Quote } from './quote.model';
import { QuotesService } from './quotes.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [FormsModule, DatePipe, DecimalPipe],
  templateUrl: './app.component.html',
  styleUrl: './app.css',
})
export class AppComponent {
  private service = inject(QuotesService);

  currentPage = signal(1);
  pageSize    = signal(10);
  searchTerm  = signal('');
  quotes       = signal<Quote[]>([]);
  isLoading    = signal(false);
  errorMessage = signal<string | null>(null);
  totalQuotes  = signal(0);

  private lastSearch = '';

  filteredQuotes = computed(() => this.quotes());
  totalCount     = computed(() => this.filteredQuotes().length);
  totalPages     = computed(() => Math.max(1, Math.ceil(this.totalQuotes() / this.pageSize())));
  pageStart      = computed(() => (this.currentPage() - 1) * this.pageSize() + 1);
  isLastPage     = computed(() => this.currentPage() >= this.totalPages());

  summary = computed(() => {
    const term = this.searchTerm();
    const base = 'Showing ' + this.totalCount() + ' quotes · Page ' + this.currentPage() + ' of ' + this.totalPages();
    return term ? base + ' · filtered by "' + term + '"' : base;
  });

  constructor() {
    effect(() => {
      const page   = this.currentPage();
      const size   = this.pageSize();
      const search = this.searchTerm();

      // Reset to page 1 whenever the search term changes
      if (search !== this.lastSearch) {
        this.lastSearch = search;
        if (page !== 1) {
          this.currentPage.set(1);
          return;
        }
      }

      console.log(`[effect] Fetching page=${page} size=${size} search="${search}"`);

      this.isLoading.set(true);
      this.errorMessage.set(null);

      this.service.getQuotes(page, size, search).subscribe({
        next: response => {
          this.quotes.set(response.data);
          this.totalQuotes.set(response.pagination.total);
          this.isLoading.set(false);
        },
        error: err => {
          this.errorMessage.set(
            'Failed to load quotes: ' + (err.message ?? err.statusText ?? 'Unknown error')
          );
          this.isLoading.set(false);
        },
      });
    });
  }

  prevPage(): void {
    if (this.currentPage() > 1) this.currentPage.update(p => p - 1);
  }

  nextPage(): void {
    if (!this.isLastPage()) this.currentPage.update(p => p + 1);
  }
}
