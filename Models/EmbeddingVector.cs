namespace FileWise.Models;

public class EmbeddingVector
{
    public int Id { get; set; }
    public int FileMetadataId { get; set; }
    public string ChunkText { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string EmbeddingJson { get; set; } = string.Empty; // JSON array of floats
    public DateTime CreatedDate { get; set; }
}

