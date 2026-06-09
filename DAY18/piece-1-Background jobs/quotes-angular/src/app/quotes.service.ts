import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Quote, QuotesApiResponse } from './quote.model';
import { environment } from '../environments/environment';

@Injectable({ providedIn: 'root' })
export class QuotesService {
  private http    = inject(HttpClient);
  private base    = environment.apiBase;

  getQuotes(page: number, size: number, search: string = ''): Observable<QuotesApiResponse> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('size', size.toString());
    if (search.trim()) {
      params = params.set('search', search.trim());
    }
    return this.http.get<QuotesApiResponse>(`${this.base}/api/quotes`, { params });
  }

  getQuote(id: number): Observable<Quote> {
    return this.http.get<Quote>(`${this.base}/api/quotes/${id}`);
  }

  createQuote(payload: { author: string; text: string }): Observable<Quote> {
    return this.http.post<Quote>(`${this.base}/api/quotes`, payload);
  }

  deleteQuote(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/api/quotes/${id}`);
  }
}
