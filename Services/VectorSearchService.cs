using FileWise.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FileWise.Services;

public class VectorSearchService : IVectorSearchService
{
    private readonly IDatabaseService _databaseService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<VectorSearchService> _logger;

    public VectorSearchService(
        IDatabaseService databaseService,
        IEmbeddingService embeddingService,
        ILogger<VectorSearchService> logger)
    {
        _databaseService = databaseService;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<List<SearchResult>> SearchAsync(string query, int topK = 5)
    {
        try
        {
            // Generate embedding for query
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);

            // Get all embeddings from database
            var allEmbeddings = await _databaseService.GetAllEmbeddingsAsync();
            var files = await _databaseService.GetAllFilesAsync();
            var fileDict = files.ToDictionary(f => f.Id);

            // Calculate cosine similarity
            var similarities = new List<(EmbeddingVector embedding, double similarity, FileMetadata file)>();

            foreach (var embedding in allEmbeddings)
            {
                var storedEmbedding = JsonSerializer.Deserialize<float[]>(embedding.EmbeddingJson);
                if (storedEmbedding == null) continue;

                var similarity = CosineSimilarity(queryEmbedding, storedEmbedding);
                if (fileDict.TryGetValue(embedding.FileMetadataId, out var file))
                {
                    similarities.Add((embedding, similarity, file));
                }
            }

            // Sort by similarity and take top K
            var topResults = similarities
                .OrderByDescending(s => s.similarity)
                .Take(topK)
                .Select(s => new SearchResult
                {
                    File = s.file,
                    SimilarityScore = s.similarity,
                    MatchedChunk = s.embedding.ChunkText
                })
                .ToList();

            return topResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing vector search");
            throw;
        }
    }

    private static double CosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
            throw new ArgumentException("Vectors must have the same length");

        double dotProduct = 0.0;
        double magnitudeA = 0.0;
        double magnitudeB = 0.0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            magnitudeA += vectorA[i] * vectorA[i];
            magnitudeB += vectorB[i] * vectorB[i];
        }

        magnitudeA = Math.Sqrt(magnitudeA);
        magnitudeB = Math.Sqrt(magnitudeB);

        if (magnitudeA == 0.0 || magnitudeB == 0.0)
            return 0.0;

        return dotProduct / (magnitudeA * magnitudeB);
    }
}

