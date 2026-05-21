namespace QuotesApi.Models;

// Value object — immutable, no ID, equality by value
public record CollectionItem(int QuoteId, DateTime AddedAt);
