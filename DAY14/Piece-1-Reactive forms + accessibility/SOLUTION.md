# Day 14 — Piece 1: Reactive Forms + Accessibility

---

## Screenshot Proof

All screenshots captured via Playwright against the live app (`http://localhost:4201`) with the real API (`http://localhost:5051`) running.

| # | File | What it proves |
|---|------|---------------|
| 1 | [01-homepage-list.png](screenshots/01-homepage-list.png) | App loads, quotes list visible, no form shown |
| 2 | [02-add-quote-login-form.png](screenshots/02-add-quote-login-form.png) | Click "+ Add Quote" → login form appears in right panel |
| 3 | [03-after-login-create-form.png](screenshots/03-after-login-create-form.png) | After login → create form shown with Author + Quote Text fields |
| 4 | [04-validation-errors.png](screenshots/04-validation-errors.png) | **Invalid state** — submit empty → "Author is required" + "Quote text is required" + red borders |
| 5 | [05-form-filled.png](screenshots/05-form-filled.png) | Form filled with valid data, char counts updating (4/200, 28/1000) |
| 6 | [06-submitting-saving.png](screenshots/06-submitting-saving.png) | **Submitting state** — button shows "Saving…", fields disabled |
| 7 | [07-success.png](screenshots/07-success.png) | **Success state** — form closed, quote added, list resets to "+ Add Quote" |
| 8 | [08-server-error.png](screenshots/08-server-error.png) | **Server-error state** — red "Internal Server Error" banner, form re-enabled for retry |
| 9 | [09-quote-detail-drawer.png](screenshots/09-quote-detail-drawer.png) | Click a quote → drawer slides in from right with detail + ✕ close button |

---

## Part 1 — Brief (prompt given to the agent)

**Endpoint:**
`POST http://localhost:5051/api/quotes`

**Real field names — use exactly these, nothing else:**
- `author` — string
- `text` — string

No other fields. Do NOT invent `title`, `category`, `tags`, or any other field.

**Real field constraints — validators must match exactly:**
- `author` → required, maxLength 200
- `text` → required, maxLength 1000

**Requirements given to the agent:**
- Standalone Angular 21, zoneless (`provideZonelessChangeDetection()`), no NgModule
- `inject()` everywhere — no constructor parameter injection
- `FormBuilder` + `ReactiveFormsModule` — no `ngModel`, no template-driven forms
- Full TypeScript types — no `any`
- Four form states: empty / invalid (touched) / submitting (form disabled) / server-error (re-enabled)
- Full ARIA wiring:
  - `<label for="author">` linked to `<input id="author">`
  - `<label for="text">` linked to `<textarea id="text">`
  - `[attr.aria-invalid]="ctrl.touched && ctrl.invalid ? 'true' : null"` on each field
  - `aria-describedby="author-error"` on the author input
  - `aria-describedby="text-error"` on the text textarea
  - Error span `id` must match `aria-describedby` exactly
  - `role="alert"` on each error span
  - `[attr.aria-busy]="isSubmitting() ? 'true' : null"` on submit button
  - On submit with errors → focus moves to first invalid field
- Character count below each field (e.g. `4 / 200 characters`)
- Signals: `isSubmitting`, `serverError`, `isSuccess`
- Submit logic order: markAllAsTouched → focus first invalid → form.disable() → POST → reset/enable or error/enable

---

## Part 2 — Agent Output

### New files created

#### `src/app/create-quote/create-quote.component.ts`

```typescript
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

  // Login form
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
      next: () => { this.loginForm.enable(); this.isLoggingIn.set(false); },
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

  // Create-quote form
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
      if (this.authorCtrl.invalid) { this.authorInput.nativeElement.focus(); }
      else if (this.textCtrl.invalid) { this.textInput.nativeElement.focus(); }
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
}
```

#### `src/app/create-quote/create-quote.component.html` (key a11y wiring)

```html
<!-- author field -->
<label class="field-label" for="author">Author</label>
<input
  #authorInput
  id="author"
  type="text"
  class="field-input"
  formControlName="author"
  [attr.aria-invalid]="authorCtrl.touched && authorCtrl.invalid ? 'true' : null"
  aria-describedby="author-error"
/>
<span id="author-error" class="field-error" role="alert">
  @if (authorCtrl.touched && authorCtrl.hasError('required')) { Author is required }
  @else if (authorCtrl.touched && authorCtrl.hasError('maxlength')) {
    Author must be 200 characters or less
  }
</span>
<span class="char-count">{{ authorLength() }} / 200 characters</span>

<!-- text field -->
<label class="field-label" for="text">Quote Text</label>
<textarea
  #textInput
  id="text"
  class="field-textarea"
  formControlName="text"
  [attr.aria-invalid]="textCtrl.touched && textCtrl.invalid ? 'true' : null"
  aria-describedby="text-error"
></textarea>
<span id="text-error" class="field-error" role="alert">
  @if (textCtrl.touched && textCtrl.hasError('required')) { Quote text is required }
  @else if (textCtrl.touched && textCtrl.hasError('maxlength')) {
    Quote text must be 1000 characters or less
  }
</span>
<span class="char-count">{{ textLength() }} / 1000 characters</span>

<!-- submit -->
<button
  type="submit"
  [disabled]="isSubmitting()"
  [attr.aria-busy]="isSubmitting() ? 'true' : null"
>
  {{ isSubmitting() ? 'Saving…' : 'Add Quote' }}
</button>
```

#### `src/app/quotes.service.ts` — only change: added `createQuote()`

```typescript
createQuote(payload: { author: string; text: string }): Observable<Quote> {
  return this.http.post<Quote>('/api/quotes', payload);
}
```

#### `src/app/auth.service.ts` — new (JWT login/logout)

```typescript
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http      = inject(HttpClient);
  private readonly TOKEN_KEY = 'auth_token';

  readonly token      = signal<string | null>(localStorage.getItem(this.TOKEN_KEY));
  readonly isLoggedIn = computed(() => this.token() !== null);

  login(email: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/login', { email, password }).pipe(
      tap(res => {
        localStorage.setItem(this.TOKEN_KEY, res.access_token);
        this.token.set(res.access_token);
      })
    );
  }

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    this.token.set(null);
  }
}
```

#### `src/app/auth.interceptor.ts` — new (attaches Bearer token, clears on 401)

```typescript
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth  = inject(AuthService);
  const token = auth.token();
  const authReq = token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;
  return next(authReq).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401) { auth.logout(); }
      return throwError(() => err);
    })
  );
};
```

---

## Part 3 — Verification Log

### States exercised

| State | How tested |
|---|---|
| Empty | Page load — both fields blank, no errors shown |
| Invalid | Tabbed through Author then Text without typing — "Author is required" and "Quote text is required" appeared; fields got red border |
| Too long | Pasted 201-character string in Author — "Author must be 200 characters or less" appeared |
| Submitting | Valid form submitted — button showed "Saving…", both fields disabled, API call in flight |
| Server-error | API offline — red banner appeared with error message, form re-enabled for retry |
| Login required | Opened create panel without token — login form shown, pre-filled with seeded credentials |
| 401 on POST | Token expired mid-session — interceptor cleared token, form reverted to login state |

### Keyboard path

Tab → Author input → Tab → Quote Text → Tab → Add Quote button → Enter to submit. No mouse required at any point.

### A11y checks

- `<label for="author">` links to `<input id="author">` ✓
- `<label for="text">` links to `<textarea id="text">` ✓
- `aria-invalid="true"` set when touched + invalid ✓
- `aria-describedby="author-error"` → `<span id="author-error" role="alert">` ✓
- `aria-describedby="text-error"` → `<span id="text-error" role="alert">` ✓
- Focus moves to Author on submit-with-errors ✓

> **Screenshot evidence for all four states:**
> - Empty form: [01-homepage-list.png](screenshots/01-homepage-list.png)
> - Validation errors: [04-validation-errors.png](screenshots/04-validation-errors.png)
> - Submitting / Saving: [06-submitting-saving.png](screenshots/06-submitting-saving.png)
> - Server error: [08-server-error.png](screenshots/08-server-error.png)
> - Success (form closes, quote saved): [07-success.png](screenshots/07-success.png)

### Bug caught and fixed

**What the agent got wrong:** char counts used plain method calls reading `FormControl.value` directly in the template:

```typescript
// agent's original — broken in zoneless
authorCharCount(): number { return this.authorCtrl.value?.length ?? 0; }
```

`FormControl.value` is not a signal. In a zoneless app (`provideZonelessChangeDetection()`), Angular's change detector only re-runs when a signal changes. Reading `.value` in a template method call produces no subscription, so the count was always `0 / 200` regardless of what you typed.

**Fix applied:**

```typescript
// correct — signal-based, zoneless-safe
readonly authorLength = toSignal(
  this.form.controls.author.valueChanges.pipe(map(v => (v ?? '').length)),
  { initialValue: 0 }
);
```

`toSignal` wraps the `valueChanges` Observable as an Angular signal. The template reads `authorLength()` — a real signal — so the zoneless scheduler re-renders on every keystroke.

### What breaks this form if the contract changes

1. **Field renamed** (`text` → `content`): three places break independently — `formControlName="text"` in the template, `aria-describedby="text-error"` / `id="text-error"` wiring, and the `{ author, text }` payload in `quotes.service.ts`. No single source of truth ties them together.

2. **New required field added** (e.g. `category`): the form has no `category` control so it silently submits without it. The API returns 422 Unprocessable Entity. The user sees a server-error banner with no field-level guidance about what is missing.

3. **maxLength tightened** (e.g. author 200 → 100): the client validator still allows up to 200 characters. The form says valid, the API returns 422. The validator constant in the component and the API constraint in `CreateQuoteRequestValidator.cs` are completely decoupled — changing one does not update the other.

---

## GitHub

**Branch:** `day14/reactive-forms-a11y`
**Repo:** `https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1`
**Folder:** `DAY14/Piece-1-Reactive forms + accessibility/quotes-angular/src/app/`

> Branch pushed and committed — open the link above to confirm files are visible before pasting into the form.
