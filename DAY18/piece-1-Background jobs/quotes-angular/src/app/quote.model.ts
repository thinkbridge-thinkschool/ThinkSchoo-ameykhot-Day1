export interface Quote {
  id: number;
  author: string;
  text: string;
  createdAt: string;
}

export interface QuotesPagination {
  page: number;
  size: number;
  total: number;
}

export interface QuotesApiResponse {
  data: Quote[];
  pagination: QuotesPagination;
}
