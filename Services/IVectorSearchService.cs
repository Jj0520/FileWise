using FileWise.Models;

namespace FileWise.Services;

public interface IVectorSearchService
{
    Task<List<SearchResult>> SearchAsync(string query, int topK = 5);
}

