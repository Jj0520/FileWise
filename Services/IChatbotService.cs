using FileWise.Models;

namespace FileWise.Services;

public interface IChatbotService
{
    Task<string> GetResponseAsync(string query, List<SearchResult> searchResults, List<FileMetadata>? allIndexedFiles = null);
    Task<string> GetCasualResponseAsync(string query, List<ChatMessage> conversationHistory);
    bool IsFileSearchQuery(string query);
}

