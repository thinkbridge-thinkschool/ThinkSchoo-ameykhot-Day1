import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Quote, QuotesApiResponse } from './quote.model';

@Injectable({ providedIn: 'root' })
export class QuotesService {
  private http = inject(HttpClient);

  getQuotes(page: number, size: number, search: string = ''): Observable<QuotesApiResponse> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('size', size.toString());
    if (search.trim()) {
      params = params.set('search', search.trim());
    }
    return this.http.get<QuotesApiResponse>('/api/quotes', { params });
  }

  getQuote(id: number): Observable<Quote> {
    return this.http.get<Quote>(`/api/quotes/${id}`);
  }
}
