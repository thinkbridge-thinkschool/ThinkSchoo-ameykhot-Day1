import { Injectable, signal } from '@angular/core';
import { Quote } from './quote.model';

@Injectable({ providedIn: 'root' })
export class StarService {
  private readonly KEY = 'quotes-starred';

  starred = signal<Quote[]>(
    JSON.parse(localStorage.getItem(this.KEY) ?? '[]') as Quote[]
  );

  isStarred(id: number): boolean {
    return this.starred().some(q => q.id === id);
  }

  toggle(quote: Quote): void {
    const curr = this.starred();
    const next = curr.some(q => q.id === quote.id)
      ? curr.filter(q => q.id !== quote.id)
      : [...curr, quote];
    this.starred.set(next);
    localStorage.setItem(this.KEY, JSON.stringify(next));
  }
}
