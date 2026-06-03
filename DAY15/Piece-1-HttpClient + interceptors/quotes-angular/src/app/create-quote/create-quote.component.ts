import { Component, ElementRef, inject, output, signal, ViewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { AuthService } from '../auth.service';
import { QuotesService } from '../quotes.service';

@Component({
  selector: 'app-create-quote',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './create-quote.component.html',
  styleUrl: './create-quote.component.css',
})
export class CreateQuoteComponent {
  private readonly fb   = inject(FormBuilder);
  private readonly svc  = inject(QuotesService);
  readonly auth         = inject(AuthService);

  @ViewChild('authorInput') private authorInput!: ElementRef<HTMLInputElement>;
  @ViewChild('textInput')   private textInput!: ElementRef<HTMLTextAreaElement>;

  readonly closed = output<void>();

  // ── Login form ──────────────────────────────────────────────────────
  readonly loginForm = this.fb.group({
    email:    ['user@test.com', [Validators.required, Validators.email]],
    password: ['password123',   [Validators.required]],
  });
  readonly isLoggingIn  = signal(false);
  readonly loginError   = signal<string | null>(null);

  get emailCtrl()    { return this.loginForm.controls.email; }
  get passwordCtrl() { return this.loginForm.controls.password; }

  onLogin(): void {
    this.loginForm.markAllAsTouched();
    if (this.loginForm.invalid) return;

    this.isLoggingIn.set(true);
    this.loginError.set(null);
    this.loginForm.disable();

    const { email, password } = this.loginForm.getRawValue();
    this.auth.login(email!, password!).subscribe({
      next: () => {
        this.loginForm.enable();
        this.isLoggingIn.set(false);
      },
      error: (err: unknown) => {
        const msg = err instanceof HttpErrorResponse && err.status === 401
          ? 'Invalid email or password.'
          : 'Login failed. Please try again.';
        this.loginError.set(msg);
        this.loginForm.enable();
        this.isLoggingIn.set(false);
      },
    });
  }

  // ── Create-quote form ───────────────────────────────────────────────
  readonly isSubmitting = signal(false);
  readonly serverError  = signal<string | null>(null);
  readonly isSuccess    = signal(false);

  readonly form = this.fb.group({
    author: ['', [Validators.required, Validators.maxLength(200)]],
    text:   ['', [Validators.required, Validators.maxLength(1000)]],
  });

  get authorCtrl() { return this.form.controls.author; }
  get textCtrl()   { return this.form.controls.text; }

  readonly authorLength = toSignal(
    this.form.controls.author.valueChanges.pipe(map(v => (v ?? '').length)),
    { initialValue: 0 }
  );
  readonly textLength = toSignal(
    this.form.controls.text.valueChanges.pipe(map(v => (v ?? '').length)),
    { initialValue: 0 }
  );

  onSubmit(): void {
    this.form.markAllAsTouched();

    if (this.form.invalid) {
      if (this.authorCtrl.invalid) {
        this.authorInput.nativeElement.focus();
      } else if (this.textCtrl.invalid) {
        this.textInput.nativeElement.focus();
      }
      return;
    }

    this.isSubmitting.set(true);
    this.serverError.set(null);
    this.form.disable();

    const { author, text } = this.form.getRawValue();

    this.svc.createQuote({ author: author!, text: text! }).subscribe({
      next: () => {
        this.form.reset();
        this.form.enable();
        this.isSubmitting.set(false);
        this.isSuccess.set(true);
        setTimeout(() => this.closed.emit(), 1800);
      },
      error: (err: unknown) => {
        const msg = err instanceof HttpErrorResponse
          ? (err.error?.title ?? err.error?.detail ?? err.message ?? 'Server error')
          : 'Failed to create quote. Please try again.';
        this.serverError.set(msg);
        this.form.enable();
        this.isSubmitting.set(false);
      },
    });
  }

  resetSuccess(): void {
    this.isSuccess.set(false);
    this.serverError.set(null);
  }
}
