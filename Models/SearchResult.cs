namespace FileWise.Models;

public class SearchResult
{
    public FileMetadata File { get; set; } = null!;
    public double SimilarityScore { get; set; }
    public string MatchedChunk { get; set; } = string.Empty;
}

