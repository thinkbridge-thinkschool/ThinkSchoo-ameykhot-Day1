import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="nf-page">
      <div class="nf-card">
        <div class="nf-icon">404</div>
        <h1 class="nf-title">Page Not Found</h1>
        <p class="nf-desc">The page you're looking for doesn't exist.</p>
        <a class="nf-btn" routerLink="/quotes">← Back to Quotes</a>
      </div>
    </div>
  `,
  styles: [`
    .nf-page {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      padding: 2rem 1.25rem;
      position: relative;
      z-index: 1;
    }
    .nf-card {
      text-align: center;
      max-width: 420px;
      animation: fadeUp 0.5s ease both;
    }
    .nf-icon {
      font-family: 'Playfair Display', serif;
      font-size: 6rem;
      font-weight: 700;
      background: linear-gradient(135deg, #f59e0b 0%, #ec4899 50%, #7c3aed 100%);
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
      line-height: 1;
      margin-bottom: 1rem;
    }
    .nf-title {
      font-family: 'Playfair Display', serif;
      font-size: 1.75rem;
      font-weight: 700;
      color: var(--text);
      margin-bottom: 0.75rem;
    }
    .nf-desc { color: var(--muted); font-size: 0.95rem; margin-bottom: 2rem; }
    .nf-btn {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.6rem 1.5rem;
      border-radius: 50px;
      border: 1px solid rgba(245,158,11,0.45);
      background: rgba(245,158,11,0.1);
      color: var(--gold);
      text-decoration: none;
      font-size: 0.88rem;
      font-weight: 600;
      transition: background 0.2s, border-color 0.2s;
    }
    .nf-btn:hover { background: rgba(245,158,11,0.2); border-color: var(--gold); }
    @keyframes fadeUp {
      from { opacity: 0; transform: translateY(20px); }
      to   { opacity: 1; transform: translateY(0); }
    }
  `],
})
export class NotFoundComponent {}
