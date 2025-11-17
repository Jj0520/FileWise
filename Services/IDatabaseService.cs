using FileWise.Models;

namespace FileWise.Services;

public interface IDatabaseService
{
    Task InitializeAsync();
    Task<int> InsertFileMetadataAsync(FileMetadata metadata);
    Task UpdateFileMetadataAsync(FileMetadata metadata);
    Task<FileMetadata?> GetFileMetadataByPathAsync(string filePath);
    Task<List<FileMetadata>> GetAllFilesAsync();
    Task InsertEmbeddingAsync(EmbeddingVector embedding);
    Task<List<EmbeddingVector>> GetEmbeddingsByFileIdAsync(int fileMetadataId);
    Task<List<EmbeddingVector>> GetAllEmbeddingsAsync();
    Task<bool> FileExistsAsync(string filePath, string hash);
    Task DeleteEmbeddingsByFileIdAsync(int fileMetadataId);
    Task DeleteFileMetadataAsync(int fileMetadataId);
}

