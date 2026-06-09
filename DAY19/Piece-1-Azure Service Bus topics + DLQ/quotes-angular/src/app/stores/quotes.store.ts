/*
WHEN TO MOVE FROM SIGNALS TO NGRX:

Move when TWO OR MORE of these are true:
  1. SHARED ACROSS ≥3 FEATURES — quotes state is read/written by 3+ unrelated
     feature modules (not just components within the same feature). Signals in a
     service still work at 2 features; NgRx slice isolation pays off at 3+.
  2. COMPLEX ASYNC CHOREOGRAPHY — you need coordinated multi-step flows
     (e.g. optimistic update → server confirm → compensating rollback) where a
     linear effect chain becomes hard to follow and test in isolation.
  3. TIME-TRAVEL / REPLAY DEBUGGING — a bug that only reproduces after a specific
     sequence of 10+ actions; the Redux DevTools replay capability is worth the
     boilerplate at that point.
  4. TEAM SIZE ≥5 ENGINEERS touching the same state — NgRx's explicit action
     names, typed reducers, and selectors act as a shared contract that prevents
     silent collisions between concurrent contributors.
  5. CROSS-FEATURE STATE DERIVATION — you have computed selectors that JOIN data
     from two or more independent feature stores (e.g. quotes + user-preferences
     + analytics) and those derivations need memoisation beyond computed().

Rule of thumb: a single signal store service like this one is correct for a
solo feature owned by one team. The moment state is SHARED, CHOREOGRAPHED, or
DEBUGGED across multiple features or engineers, NgRx pays for its cost in clarity.
*/

import { Injectable, computed, inject, signal } from '@angular/core';
import { Quote } from '../quote.model';
import { QuotesService } from '../quotes.service';

@Injectable({ providedIn: 'root' })
export class QuotesStore {
  private readonly api = inject(QuotesService);

  // ── Private mutable signals ──────────────────────────────────────────────
  private readonly _quotes        = signal<Quote[]>([]);
  private readonly _selectedQuote = signal<Quote | null>(null);
  private readonly _isLoading     = signal(false);
  private readonly _error         = signal<string | null>(null);
  private readonly _currentPage   = signal(1);
  private readonly _pageSize      = signal(10);
  private readonly _total         = signal(0);

  // ── Public readonly (components read, never write directly) ──────────────
  readonly quotes        = this._quotes.asReadonly();
  readonly selectedQuote = this._selectedQuote.asReadonly();
  readonly isLoading     = this._isLoading.asReadonly();
  readonly error         = this._error.asReadonly();
  readonly currentPage   = this._currentPage.asReadonly();
  readonly pageSize      = this._pageSize.asReadonly();
  readonly total         = this._total.asReadonly();

  // ── Computed ─────────────────────────────────────────────────────────────
  readonly totalCount = computed(() => this._quotes().length);
  readonly hasError   = computed(() => this._error() !== null);
  readonly isEmpty    = computed(() => !this._isLoading() && this._quotes().length === 0);

  // ── Actions ───────────────────────────────────────────────────────────────

  loadQuotes(search: string = ''): void {
    console.log('[QuotesStore] loadQuotes → isLoading=true');
    this._isLoading.set(true);
    this._error.set(null);

    this.api
      .getQuotes(this._currentPage(), this._pageSize(), search)
      .subscribe({
        next: (response) => {
          this._quotes.set(response.data);
          this._total.set(response.pagination.total);
          this._isLoading.set(false);
          console.log(`[QuotesStore] loadQuotes ✓ quotes=${response.data.length} total=${response.pagination.total}`);
        },
        error: (err: Error) => {
          const msg = err.message ?? 'Unknown error';
          this._error.set('Failed to load quotes: ' + msg);
          this._isLoading.set(false);
          console.error(`[QuotesStore] loadQuotes ✗ error=${msg}`);
        },
      });
  }

  loadQuote(id: number): void {
    this._isLoading.set(true);
    this._error.set(null);

    this.api.getQuote(id).subscribe({
      next: (quote) => {
        this._selectedQuote.set(quote);
        this._isLoading.set(false);
      },
      error: (err: Error) => {
        this._error.set('Failed to load quote: ' + (err.message ?? 'Unknown error'));
        this._isLoading.set(false);
      },
    });
  }

  addQuote(author: string, text: string): void {
    this._isLoading.set(true);
    this._error.set(null);

    this.api.createQuote({ author, text }).subscribe({
      next: () => {
        this._isLoading.set(false);
        // Refresh list so newly added quote appears immediately
        this.loadQuotes();
      },
      error: (err: Error) => {
        this._error.set('Failed to add quote: ' + (err.message ?? 'Unknown error'));
        this._isLoading.set(false);
      },
    });
  }

  deleteQuote(id: number): void {
    this._isLoading.set(true);
    this._error.set(null);

    this.api.deleteQuote(id).subscribe({
      next: () => {
        // Remove locally — no need to re-fetch the whole page
        this._quotes.update((qs) => qs.filter((q) => q.id !== id));
        this._isLoading.set(false);
      },
      error: (err: Error) => {
        this._error.set('Failed to delete quote: ' + (err.message ?? 'Unknown error'));
        this._isLoading.set(false);
      },
    });
  }

  setPage(page: number): void {
    this._currentPage.set(page);
  }

  clearError(): void {
    this._error.set(null);
  }
}
