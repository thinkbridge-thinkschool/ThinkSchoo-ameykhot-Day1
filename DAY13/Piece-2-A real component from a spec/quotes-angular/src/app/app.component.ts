import { Component, signal } from '@angular/core';
import { QuotesListComponent } from './quotes-list/quotes-list.component';
import { QuoteDetailComponent } from './quote-detail/quote-detail.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [QuotesListComponent, QuoteDetailComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.css',
})
export class AppComponent {
  selectedQuoteId = signal<number | null>(null);
}
