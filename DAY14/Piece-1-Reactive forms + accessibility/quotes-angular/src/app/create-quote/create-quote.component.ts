import { Component, ElementRef, inject, signal, ViewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { QuotesService } from '../quotes.service';

@Component({
  selector: 'app-create-quote',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './create-quote.component.html',
  styleUrl: './create-quote.component.css',
})
export class CreateQuoteComponent {
  private readonly fb  = inject(FormBuilder);
  private readonly svc = inject(QuotesService);

  @ViewChild('authorInput') private authorInput!: ElementRef<HTMLInputElement>;
  @ViewChild('textInput')   private textInput!: ElementRef<HTMLTextAreaElement>;

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
    // Step 1: mark all touched so errors render
    this.form.markAllAsTouched();

    // Step 2: if invalid → focus first invalid field → stop
    if (this.form.invalid) {
      if (this.authorCtrl.invalid) {
        this.authorInput.nativeElement.focus();
      } else if (this.textCtrl.invalid) {
        this.textInput.nativeElement.focus();
      }
      return;
    }

    // Step 3: start submitting
    this.isSubmitting.set(true);
    this.serverError.set(null);
    this.form.disable();

    const { author, text } = this.form.getRawValue();

    // Step 4: POST /api/quotes
    this.svc.createQuote({ author: author!, text: text! }).subscribe({
      next: () => {
        // Step 5: success → reset + show confirmation
        this.form.reset();
        this.form.enable();
        this.isSubmitting.set(false);
        this.isSuccess.set(true);
      },
      error: (err: unknown) => {
        // Step 6: show server error, re-enable so user can retry
        const msg = err instanceof HttpErrorResponse
          ? (err.error?.title ?? err.error?.detail ?? err.message ?? 'Server error')
          : 'Failed to create quote. Please try again.';
        this.serverError.set(msg);
        this.form.enable();
        // Step 7: always stop submitting
        this.isSubmitting.set(false);
      },
    });
  }

  resetSuccess(): void {
    this.isSuccess.set(false);
    this.serverError.set(null);
  }
}
