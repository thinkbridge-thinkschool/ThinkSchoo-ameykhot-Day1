import { Component, signal } from '@angular/core';
import { QuotesListComponent } from './quotes-list/quotes-list.component';
import { QuoteDetailComponent } from './quote-detail/quote-detail.component';
import { CreateQuoteComponent } from './create-quote/create-quote.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [QuotesListComponent, QuoteDetailComponent, CreateQuoteComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.css',
})
export class AppComponent {
  selectedQuoteId = signal<number | null>(null);
}
