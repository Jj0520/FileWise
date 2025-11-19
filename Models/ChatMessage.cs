namespace FileWise.Models;

public class ChatMessage
{
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public DateTime Timestamp { get; set; }
    public List<SearchResult>? RelatedFiles { get; set; }
    public string? ImagePath { get; set; }
}

