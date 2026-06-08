import { HttpInterceptorFn } from '@angular/common/http';
import { retry, timer } from 'rxjs';

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: 3,
      delay: (error, retryCount) => {
        // Do not retry on 4xx client errors
        if (error.status >= 400 && error.status < 500) {
          throw error;
        }
        const delayMs = Math.pow(2, retryCount - 1) * 1000;
        console.log(`[retry] attempt ${retryCount} after ${delayMs}ms`);
        return timer(delayMs);
      },
    })
  );
};
