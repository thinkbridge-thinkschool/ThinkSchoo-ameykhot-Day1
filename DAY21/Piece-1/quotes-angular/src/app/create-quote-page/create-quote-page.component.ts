import { Component, effect, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CreateQuoteComponent } from '../create-quote/create-quote.component';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-create-quote-page',
  standalone: true,
  imports: [CreateQuoteComponent, RouterLink],
  templateUrl: './create-quote-page.component.html',
  styleUrl: './create-quote-page.component.css',
})
export class CreateQuotePageComponent {
  private readonly router = inject(Router);
  readonly auth           = inject(AuthService);

  constructor() {
    // If user logs out while on this page, redirect to login
    effect(() => {
      if (!this.auth.isLoggedIn()) {
        this.router.navigate(['/login', { reason: 'unauthenticated' }]);
      }
    });
  }

  onQuoteCreated(): void {
    this.router.navigate(['/quotes']);
  }
}
