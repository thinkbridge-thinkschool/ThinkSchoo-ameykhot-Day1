using QuotesApi.Models;

namespace QuotesApi.Services;

public interface IQuoteFactory
{
    Quote Create(string author, string text);
}