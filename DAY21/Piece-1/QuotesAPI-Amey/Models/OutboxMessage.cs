namespace QuotesApi.Models;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty;

    // Serialised JSON payload — stored as-is and forwarded to Service Bus by the relay
    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Null = not yet relayed; set to UtcNow by relay after a successful SendMessageAsync
    public DateTime? ProcessedAt { get; set; }

    // Incremented on each failed publish attempt; relay skips rows with RetryCount >= 5
    public int RetryCount { get; set; }

    public string? Error { get; set; }
}
