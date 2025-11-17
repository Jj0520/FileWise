using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileWise.Services;

/// <summary>
/// Global rate limiter for all Gemini API calls to prevent exceeding API limits
/// </summary>
public static class GeminiRateLimiter
{
    private static readonly SemaphoreSlim _globalSemaphore = new SemaphoreSlim(1, 1); // Only 1 request at a time globally
    private static DateTime _lastRequestTime = DateTime.MinValue;
    private static readonly TimeSpan _minTimeBetweenRequests = TimeSpan.FromSeconds(4); // 4 seconds = 15 requests per minute max
    
    /// <summary>
    /// Wait for rate limit before making an API call
    /// </summary>
    public static async Task WaitForRateLimitAsync()
    {
        await _globalSemaphore.WaitAsync();
        try
        {
            // Ensure minimum time between requests to avoid rate limits
            var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
            if (timeSinceLastRequest < _minTimeBetweenRequests)
            {
                var delay = _minTimeBetweenRequests - timeSinceLastRequest;
                System.Diagnostics.Debug.WriteLine($"Rate limiter: Waiting {delay.TotalSeconds:F1} seconds before next API call");
                await Task.Delay(delay);
            }
            _lastRequestTime = DateTime.UtcNow;
        }
        finally
        {
            _globalSemaphore.Release();
        }
    }
    
    /// <summary>
    /// Get the minimum time between requests
    /// </summary>
    public static TimeSpan MinTimeBetweenRequests => _minTimeBetweenRequests;
}















