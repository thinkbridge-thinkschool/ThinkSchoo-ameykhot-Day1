/*
SIGNAL FORMS vs REACTIVE FORMS:
Simpler:  No FormBuilder/FormGroup/FormControl boilerplate — signalField() replaces all three;
          validators are plain functions instead of Validators.* class methods; character counts
          read directly as field.value().length with no toSignal() bridge; value/touched/error
          are uniform WritableSignals — no Observable-to-signal impedance mismatch in the template.
Rougher:  No form.disable()/enable() shorthand — [disabled] must be bound to every input
          individually; no built-in markAllAsTouched() — signalForm() must loop fields manually;
          Angular form directives (formControlName, [formGroup]) don't apply here so raw
          [value]/(input)/(blur) bindings replace the ergonomic directive approach; aria-invalid
          and describedby wiring is identical to reactive forms — zero accessibility gains;
          signalField()/signalForm() are hand-rolled preview helpers since Angular 21 ships no
          official Signal Forms API yet.
*/

import {
  Component,
  ElementRef,
  Signal,
  ViewChild,
  WritableSignal,
  computed,
  inject,
  output,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../auth.service';
import { QuotesService } from '../quotes.service';

// ─── Signal Forms preview helpers ────────────────────────────────────────────

function required(value: string): string | null {
  return value.trim() ? null : 'required';
}

function validEmail(value: string): string | null {
  if (!value.trim()) return null;
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value) ? null : 'email';
}

function maxLength(max: number): (value: string) => string | null {
  return (value: string) => (value.length <= max ? null : 'maxlength');
}

interface SignalFieldDef {
  value: WritableSignal<string>;
  touched: WritableSignal<boolean>;
  invalid: Signal<boolean>;
  error: Signal<string | null>;
  markTouched: () => void;
  reset: () => void;
}

interface SignalFormDef<T extends Record<string, SignalFieldDef>> {
  fields: T;
  invalid: Signal<boolean>;
  markAllTouched: () => void;
  reset: () => void;
}

function signalField(
  initial: string,
  validators: Array<(v: string) => string | null>,
  messages: Record<string, string>,
): SignalFieldDef {
  const value = signal(initial);
  const touched = signal(false);
  const error = computed(() => {
    for (const fn of validators) {
      const key = fn(value());
      if (key) return messages[key] ?? key;
    }
    return null;
  });
  const invalid = computed(() => error() !== null);
  return {
    value,
    touched,
    invalid,
    error,
    markTouched: () => touched.set(true),
    reset: () => {
      value.set(initial);
      touched.set(false);
    },
  };
}

function signalForm<T extends Record<string, SignalFieldDef>>(
  fields: T,
): SignalFormDef<T> {
  const fieldList = Object.values(fields);
  return {
    fields,
    invalid: computed(() => fieldList.some((f) => f.invalid())),
    markAllTouched: () => fieldList.forEach((f) => f.markTouched()),
    reset: () => fieldList.forEach((f) => f.reset()),
  };
}

// ─── Component ───────────────────────────────────────────────────────────────

@Component({
  selector: 'app-create-quote-signal',
  standalone: true,
  imports: [],
  templateUrl: './create-quote-signal.component.html',
  styleUrl: './create-quote-signal.component.css',
})
export class CreateQuoteSignalComponent {
  private readonly svc = inject(QuotesService);
  readonly auth = inject(AuthService);

  @ViewChild('authorInput') private authorInputEl!: ElementRef<HTMLInputElement>;
  @ViewChild('textInput') private textInputEl!: ElementRef<HTMLTextAreaElement>;

  readonly closed = output<void>();

  // ── Login form ──────────────────────────────────────────────────────
  readonly loginEmailField = signalField(
    'user@test.com',
    [required, validEmail],
    { required: 'Email is required', email: 'Enter a valid email' },
  );
  readonly loginPasswordField = signalField(
    'password123',
    [required],
    { required: 'Password is required' },
  );
  readonly loginForm = signalForm({
    email: this.loginEmailField,
    password: this.loginPasswordField,
  });
  readonly isLoggingIn = signal(false);
  readonly loginError = signal<string | null>(null);

  onLogin(event: Event): void {
    event.preventDefault();
    this.loginForm.markAllTouched();
    if (this.loginForm.invalid()) return;

    this.isLoggingIn.set(true);
    this.loginError.set(null);

    this.auth
      .login(this.loginEmailField.value(), this.loginPasswordField.value())
      .subscribe({
        next: () => {
          this.isLoggingIn.set(false);
        },
        error: (err: unknown) => {
          const msg =
            err instanceof HttpErrorResponse && err.status === 401
              ? 'Invalid email or password.'
              : 'Login failed. Please try again.';
          this.loginError.set(msg);
          this.isLoggingIn.set(false);
        },
      });
  }

  // ── Create-quote form ───────────────────────────────────────────────
  readonly authorField = signalField(
    '',
    [required, maxLength(200)],
    {
      required: 'Author is required',
      maxlength: 'Author must be 200 characters or less',
    },
  );
  readonly textField = signalField(
    '',
    [required, maxLength(1000)],
    {
      required: 'Quote text is required',
      maxlength: 'Quote text must be 1000 characters or less',
    },
  );
  readonly quoteForm = signalForm({
    author: this.authorField,
    text: this.textField,
  });

  readonly isSubmitting = signal(false);
  readonly serverError = signal<string | null>(null);
  readonly isSuccess = signal(false);

  onSubmit(event: Event): void {
    event.preventDefault();
    this.quoteForm.markAllTouched();

    if (this.quoteForm.invalid()) {
      if (this.authorField.invalid()) {
        this.authorInputEl.nativeElement.focus();
      } else {
        this.textInputEl.nativeElement.focus();
      }
      return;
    }

    this.isSubmitting.set(true);
    this.serverError.set(null);

    this.svc
      .createQuote({
        author: this.authorField.value(),
        text: this.textField.value(),
      })
      .subscribe({
        next: () => {
          this.quoteForm.reset();
          this.isSubmitting.set(false);
          this.isSuccess.set(true);
          setTimeout(() => this.closed.emit(), 1800);
        },
        error: (err: unknown) => {
          const msg =
            err instanceof HttpErrorResponse
              ? (err.error?.title ??
                  err.error?.detail ??
                  err.message ??
                  'Server error')
              : 'Failed to create quote. Please try again.';
          this.serverError.set(msg);
          this.isSubmitting.set(false);
        },
      });
  }

  resetSuccess(): void {
    this.isSuccess.set(false);
    this.serverError.set(null);
  }
}
