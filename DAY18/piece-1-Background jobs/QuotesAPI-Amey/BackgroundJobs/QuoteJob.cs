namespace QuotesApi.BackgroundJobs;

public class QuoteJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int QuoteId { get; init; }
    public string JobType { get; init; } = string.Empty;
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
}
