import { TestBed } from '@angular/core/testing';
import {
  HttpClientTestingModule,
  HttpTestingController,
} from '@angular/common/http/testing';
import { HttpClient } from '@angular/common/http';

const BASE = 'http://localhost:5051/api/quotes';

interface Quote {
  id: number;
  author: string;
  text: string;
  createdAt: string;
}

interface ProblemDetails {
  type?: string;
  title: string;
  status: number;
  detail?: string;
  errors?: { [field: string]: string[] };
}

describe('Quotes API Contract', () => {
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    controller.verify();
  });

  // ─── Test 1: GET /api/quotes shape ────────────────────────────────────────
  it('GET /api/quotes returns array; each item has id, author, text, createdAt and no extra fields', () => {
    const mockQuotes: Quote[] = [
      { id: 1, author: 'Alice', text: 'First quote', createdAt: '2024-01-01T00:00:00Z' },
      { id: 2, author: 'Bob', text: 'Second quote', createdAt: '2024-01-02T00:00:00Z' },
    ];

    http.get<Quote[]>(`${BASE}?page=1&size=10`).subscribe((response) => {
      expect(Array.isArray(response)).toBeTrue();

      response.forEach((item) => {
        expect(typeof item.id).toBe('number');
        expect(typeof item.author).toBe('string');
        expect(typeof item.text).toBe('string');
        expect(typeof item.createdAt).toBe('string');

        // No invented fields
        expect((item as unknown as Record<string, unknown>)['title']).toBeUndefined();
        expect((item as unknown as Record<string, unknown>)['category']).toBeUndefined();
        expect((item as unknown as Record<string, unknown>)['name']).toBeUndefined();
      });
    });

    const req = controller.expectOne(`${BASE}?page=1&size=10`);
    expect(req.request.method).toBe('GET');
    req.flush(mockQuotes);
  });

  // ─── Test 2: GET /api/quotes/{id} shape ──────────────────────────────────
  describe('GET /api/quotes/:id', () => {
    it('returns a single quote with id, author, text, createdAt when found', () => {
      const mockQuote: Quote = {
        id: 1,
        author: 'Alice',
        text: 'First quote',
        createdAt: '2024-01-01T00:00:00Z',
      };

      http.get<Quote>(`${BASE}/1`).subscribe((response) => {
        expect(typeof response.id).toBe('number');
        expect(typeof response.author).toBe('string');
        expect(typeof response.text).toBe('string');
        expect(typeof response.createdAt).toBe('string');
      });

      const req = controller.expectOne(`${BASE}/1`);
      expect(req.request.method).toBe('GET');
      req.flush(mockQuote);
    });

    it('returns ProblemDetails with status=404 and title when not found', () => {
      const notFound: ProblemDetails = {
        type: 'https://tools.ietf.org/html/rfc7231#section-6.5.4',
        title: 'Not Found',
        status: 404,
        detail: 'Quote with id 999 was not found.',
      };

      http.get<Quote>(`${BASE}/999`).subscribe({
        next: () => fail('expected 404 error'),
        error: (err) => {
          expect(err.status).toBe(404);
          expect(err.error.status).toBe(404);
          expect(typeof err.error.title).toBe('string');
        },
      });

      const req = controller.expectOne(`${BASE}/999`);
      req.flush(notFound, { status: 404, statusText: 'Not Found' });
    });
  });

  // ─── Test 3: POST /api/quotes — validation error ─────────────────────────
  it('POST /api/quotes with empty body returns 400 ValidationProblemDetails with author and text errors', () => {
    const validationError: ProblemDetails = {
      type: 'https://tools.ietf.org/html/rfc7231#section-6.5.1',
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: {
        author: ['The author field is required.'],
        text: ['The text field is required.'],
      },
    };

    http.post<Quote>(BASE, {}).subscribe({
      next: () => fail('expected 400 error'),
      error: (err) => {
        expect(err.status).toBe(400);
        expect(err.error.status).toBe(400);
        expect(typeof err.error.title).toBe('string');
        expect(err.error.errors).toBeDefined();
        expect(err.error.errors['author']).toBeDefined();
        expect(err.error.errors['text']).toBeDefined();
      },
    });

    const req = controller.expectOne(BASE);
    expect(req.request.method).toBe('POST');
    req.flush(validationError, { status: 400, statusText: 'Bad Request' });
  });

  // ─── Test 4: POST /api/quotes — success ──────────────────────────────────
  it('POST /api/quotes with valid body returns 201 with id, author, text, createdAt', () => {
    const payload = { author: 'Test Author', text: 'Test quote text' };
    const created: Quote = {
      id: 42,
      author: 'Test Author',
      text: 'Test quote text',
      createdAt: '2024-06-01T00:00:00Z',
    };

    http
      .post<Quote>(BASE, payload, { observe: 'response' })
      .subscribe((response) => {
        expect(response.status).toBe(201);
        expect(typeof response.body!.id).toBe('number');
        expect(response.body!.author).toBe('Test Author');
        expect(response.body!.text).toBe('Test quote text');
        expect(typeof response.body!.createdAt).toBe('string');
      });

    const req = controller.expectOne(BASE);
    expect(req.request.method).toBe('POST');
    req.flush(created, { status: 201, statusText: 'Created' });
  });
});
