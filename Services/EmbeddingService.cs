using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;

namespace FileWise.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<EmbeddingService> _logger;
    private const string GeminiEmbeddingApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/text-embedding-004:embedContent";

    public EmbeddingService(IConfiguration configuration, ILogger<EmbeddingService> logger)
    {
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"] ?? throw new InvalidOperationException("Gemini API key not configured");
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        try
        {
            var url = $"{GeminiEmbeddingApiUrl}?key={_apiKey}";
            
            var requestBody = new
            {
                model = "models/text-embedding-004",
                content = new
                {
                    parts = new object[]
                    {
                        new
                        {
                            text = text
                        }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Use global rate limiter to prevent conflicts with other services
            await GeminiRateLimiter.WaitForRateLimitAsync();
            
            var response = await _httpClient.PostAsync(url, content);
            
            // Handle rate limiting (429) - retry with exponential backoff
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Rate limit exceeded for embeddings, retrying in 5 seconds...");
                await Task.Delay(5000);
                
                // Retry once
                response = await _httpClient.PostAsync(url, content);
            }
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gemini embedding API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new Exception("Rate limit exceeded for embeddings. Please wait before indexing more files.");
                }
                
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new Exception("Authentication failed. Please check your Gemini API key in appsettings.json");
                }
                
                response.EnsureSuccessStatusCode();
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<dynamic>(responseJson);

            if (result?.embedding != null && result.embedding.values != null)
            {
                var values = result.embedding.values;
                var embedding = new List<float>();
                foreach (var value in values)
                {
                    embedding.Add((float)value);
                }
                return embedding.ToArray();
            }

            throw new Exception("Invalid response from Gemini embedding API");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding");
            throw;
        }
    }
}

