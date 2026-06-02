import { Component, signal } from '@angular/core';
import { QuotesListComponent } from './quotes-list/quotes-list.component';
import { QuoteDetailComponent } from './quote-detail/quote-detail.component';
import { CreateQuoteComponent } from './create-quote/create-quote.component';
import { CreateQuoteSignalComponent } from './create-quote-signal/create-quote-signal.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [QuotesListComponent, QuoteDetailComponent, CreateQuoteComponent, CreateQuoteSignalComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.css',
})
export class AppComponent {
  selectedQuoteId = signal<number | null>(null);
  showCreateForm  = signal(false);
  drawerOpen      = signal(false);
  formMode        = signal<'reactive' | 'signal'>('reactive');

  openDrawer(id: number): void {
    this.selectedQuoteId.set(id);
    this.drawerOpen.set(true);
  }

  closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  toggleCreate(): void {
    this.showCreateForm.update(v => !v);
  }

  onFormClosed(): void {
    this.showCreateForm.set(false);
  }
}
