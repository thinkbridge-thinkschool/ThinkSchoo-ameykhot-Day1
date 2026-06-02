import { Component, effect, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Quote } from '../quote.model';
import { StarService } from '../star.service';

@Component({
  selector: 'app-quote-detail',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './quote-detail.component.html',
})
export class QuoteDetailComponent {
  selectedQuoteId = input<number | null>(null);
  readonly stars  = inject(StarService);

  selectedQuote   = signal<Quote | null>(null);
  isDetailLoading = signal(false);
  detailError     = signal<string | null>(null);
  copied          = signal(false);

  private retryTrigger = signal(0);
  private controller: AbortController | null = null;
  private requestSeq = 0;

  constructor() {
    effect(() => {
      const id = this.selectedQuoteId();
      void this.retryTrigger();

      this.controller?.abort();
      this.controller = null;

      if (id === null) {
        this.selectedQuote.set(null);
        this.isDetailLoading.set(false);
        this.detailError.set(null);
        return;
      }

      const seq = ++this.requestSeq;
      const ctrl = new AbortController();
      this.controller = ctrl;

      this.isDetailLoading.set(true);
      this.detailError.set(null);
      this.selectedQuote.set(null);

      fetch(`/api/quotes/${id}`, { signal: ctrl.signal })
        .then(res => {
          if (!res.ok) throw new Error(`HTTP ${res.status}: ${res.statusText}`);
          return res.json() as Promise<Quote>;
        })
        .then(quote => {
          if (seq !== this.requestSeq) return;
          this.selectedQuote.set(quote);
          this.isDetailLoading.set(false);
        })
        .catch(err => {
          if (seq !== this.requestSeq) return;
          if (err instanceof Error && err.name === 'AbortError') return;
          this.detailError.set(err instanceof Error ? err.message : 'Failed to load quote');
          this.isDetailLoading.set(false);
        });
    });
  }

  retry(): void { this.retryTrigger.update(n => n + 1); }

  copy(): void {
    const quote = this.selectedQuote();
    if (!quote) return;
    navigator.clipboard.writeText(quote.text).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    }).catch(() => {});
  }
}
