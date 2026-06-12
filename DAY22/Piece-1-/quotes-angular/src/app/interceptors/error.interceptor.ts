import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { AppError, ProblemDetails } from '../models/app-error.model';

const FRIENDLY_MESSAGES: Record<number, string> = {
  400: 'Please check your input and try again.',
  401: 'Please log in to continue.',
  403: 'You do not have permission to do this.',
  404: 'The requested item was not found.',
  500: 'Server error. Please try again later.',
};

function isProblemDetails(body: unknown): body is ProblemDetails {
  return typeof body === 'object' && body !== null && 'title' in body;
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse)) {
        const appError: AppError = {
          status: 0,
          friendlyMessage: 'Cannot connect. Check your connection.',
          message: 'Cannot connect. Check your connection.',
        };
        return throwError(() => appError);
      }

      const status = err.status;
      let friendlyMessage =
        FRIENDLY_MESSAGES[status] ?? 'An unexpected error occurred.';
      let raw: ProblemDetails | undefined;

      if (status === 0) {
        friendlyMessage = 'Cannot connect. Check your connection.';
      } else if (isProblemDetails(err.error)) {
        raw = err.error;
        friendlyMessage = raw.detail ?? raw.title ?? friendlyMessage;
      }

      const appError: AppError = { status, friendlyMessage, message: friendlyMessage, raw };
      return throwError(() => appError);
    })
  );
};
