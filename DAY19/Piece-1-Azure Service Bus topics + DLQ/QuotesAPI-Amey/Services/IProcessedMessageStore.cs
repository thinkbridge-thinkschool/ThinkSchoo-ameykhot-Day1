namespace QuotesApi.Services;

public interface IProcessedMessageStore
{
    Task<bool> IsProcessedAsync(string messageId);
    Task MarkProcessedAsync(string messageId);
}
