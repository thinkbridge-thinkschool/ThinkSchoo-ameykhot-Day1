import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css',
})
export class LoginComponent {
  private readonly fb     = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly route  = inject(ActivatedRoute);
  readonly auth           = inject(AuthService);

  readonly showPassword = signal(false);
  readonly isLoggingIn  = signal(false);
  readonly loginError   = signal<string | null>(null);

  // Read redirect reason from query param (?reason=expired | unauthenticated)
  private readonly reason = this.route.snapshot.queryParamMap.get('reason');

  readonly banner: { message: string; type: 'expired' | 'info' } | null =
    this.reason === 'expired'
      ? { message: 'Session expired. Please log in again.', type: 'expired' }
      : this.reason === 'unauthenticated'
        ? { message: 'You are not logged in. Please log in to continue.', type: 'info' }
        : null;

  readonly form = this.fb.group({
    email:    ['user@test.com', [Validators.required, Validators.email]],
    password: ['password123',   [Validators.required]],
  });

  get emailCtrl()    { return this.form.controls.email; }
  get passwordCtrl() { return this.form.controls.password; }

  togglePassword(): void { this.showPassword.update(v => !v); }

  onLogin(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.isLoggingIn.set(true);
    this.loginError.set(null);
    this.form.disable();

    const { email, password } = this.form.getRawValue();
    this.auth.login(email!, password!).subscribe({
      next: () => {
        this.form.enable();
        this.isLoggingIn.set(false);
        this.router.navigate(['/quotes']);
      },
      error: (err: unknown) => {
        const msg = err instanceof HttpErrorResponse && err.status === 401
          ? 'Invalid email or password.'
          : 'Login failed. Please try again.';
        this.loginError.set(msg);
        this.form.enable();
        this.isLoggingIn.set(false);
      },
    });
  }
}
