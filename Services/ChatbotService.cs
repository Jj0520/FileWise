using FileWise.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;

namespace FileWise.Services;

public class ChatbotService : IChatbotService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly bool _useLocalhost;
    private readonly string _localhostUrl;
    private readonly ILogger<ChatbotService> _logger;
    private readonly UserSettingsService? _userSettingsService;
    private const string GeminiApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";

    public ChatbotService(IConfiguration configuration, ILogger<ChatbotService> logger, UserSettingsService? userSettingsService = null)
    {
        _logger = logger;
        _userSettingsService = userSettingsService;
        _useLocalhost = configuration["Gemini:UseLocalhost"]?.ToLower() == "true";
        _localhostUrl = configuration["Gemini:LocalhostUrl"] ?? "http://localhost:11434";
        
        if (!_useLocalhost)
        {
            _apiKey = configuration["Gemini:ApiKey"] ?? throw new InvalidOperationException("Gemini API key not configured");
        }
        
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(60); // Reduced from 5 minutes to 60 seconds
    }

    private string CleanResponse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return rawResponse;

        var cleaned = rawResponse.Trim();
        
        // Remove "User:" or "Erin:" prefixes if they appear at the start
        if (cleaned.StartsWith("User:", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(5).Trim();
        }
        if (cleaned.StartsWith("Erin:", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(5).Trim();
        }
        
        // Split into lines and filter out any lines that start with "User:" or "Erin:"
        var lines = cleaned.Split(new[] { '\n', '\r' }, StringSplitOptions.None);
        var cleanedLines = new List<string>();
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Skip lines that start with "User:" or "Erin:"
            if (trimmedLine.StartsWith("User:", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("Erin:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            
            // Preserve empty lines for paragraph breaks, but collapse multiple empty lines
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                // Only add empty line if previous line wasn't empty (to avoid multiple blank lines)
                if (cleanedLines.Count > 0 && !string.IsNullOrWhiteSpace(cleanedLines.Last()))
                {
                    cleanedLines.Add(string.Empty);
                }
            }
            else
            {
                cleanedLines.Add(trimmedLine);
            }
        }
        
        // Join with newlines to preserve paragraph structure
        var result = string.Join("\n", cleanedLines).Trim();
        
        // Ensure proper spacing: double newline for paragraph breaks
        // Replace single newlines within paragraphs with spaces, but keep double newlines
        result = System.Text.RegularExpressions.Regex.Replace(result, @"([^\n])\n([^\n])", "$1 $2");
        
        // Normalize multiple spaces to single space
        result = System.Text.RegularExpressions.Regex.Replace(result, @" +", " ");
        
        // Ensure double newlines for paragraph breaks
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\n\s*\n", "\n\n");
        
        return result.Trim();
    }

    public bool IsFileSearchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var lowerQuery = query.ToLower();
        
        // File search keywords (English and Chinese)
        var searchKeywords = new[]
        {
            "find", "search", "look for", "file", "document", "where is", "show me",
            "locate", "get", "retrieve", "open", "contains", "mention", "about",
            "related to", "regarding", "pertaining to", "concerning",
            "找", "搜索", "查找", "文件", "文档", "在哪里", "显示", "打开",
            "包含", "关于", "相关", "提到"
        };

        // Greetings and casual conversation keywords (English and Chinese)
        var casualKeywords = new[]
        {
            "hello", "hi", "hey", "good morning", "good afternoon", "good evening",
            "how are you", "what's up", "thanks", "thank you", "bye", "goodbye",
            "what can you do", "help", "who are you", "what is your name",
            "what's my name", "my name", "what am i called", "do you know my name",
            "can we chat", "can i chat", "chat with", "talk with", "conversation",
            "你好", "您好", "早上好", "下午好", "晚上好", "谢谢", "再见", "拜拜",
            "你能做什么", "帮助", "你是谁", "你叫什么名字", "怎么样",
            "我叫什么", "我的名字", "你知道我的名字", "我的名字是什么", "我叫什么名",
            "聊天", "可以聊天", "跟你聊天", "和你聊天", "能聊天", "聊一下", "聊一聊",
            "可以跟你聊天吗", "可以和你聊天吗", "能跟你聊天吗", "能和你聊天吗"
        };

        // Work week query keywords (English and Chinese)
        var workWeekKeywords = new[]
        {
            "work week", "week number", "what week", "current week", "this week", "week of",
            "工作周", "第几周", "本周", "这周", "星期", "周数", "今天是第几周", "现在是第几周"
        };

        // Check for work week queries first
        if (workWeekKeywords.Any(keyword => lowerQuery.Contains(keyword)))
            return false;

        // Check for casual conversation first
        if (casualKeywords.Any(keyword => lowerQuery.Contains(keyword)))
            return false;

        // Check for file search intent - only return true if explicit search keywords are found
        // Default to casual conversation if no search keywords are present
        return searchKeywords.Any(keyword => lowerQuery.Contains(keyword));
    }

    private bool IsITToolsQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var lowerQuery = query.ToLower();
        
        // IT Tools query keywords (English and Chinese)
        var itToolsKeywords = new[]
        {
            "it tools", "it user tools", "it technology tools", "user tools",
            "directory to it tools", "it tools path", "it tools directory",
            "where is it tools", "it tools location", "it tools folder",
            "new user setup", "user setup", "setup", "new user", "user setup tools",
            "IT工具", "IT用户工具", "IT技术工具", "用户工具", "IT工具路径", "IT工具目录",
            "IT工具在哪里", "IT工具位置", "IT工具文件夹", "新用户设置", "用户设置", "设置"
        };

        return itToolsKeywords.Any(keyword => lowerQuery.Contains(keyword));
    }

    private string GetITToolsPath()
    {
        return "The IT User tools directory is located at:\n\n" +
               "\\\\10.0.42.100\\Public area\\IT Technology\\User tools\n\n" +
               "You can access this network path directly to find IT tools and utilities.";
    }

    private bool IsWorkWeekQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var lowerQuery = query.ToLower();
        var workWeekKeywords = new[]
        {
            "work week", "week number", "what week", "current week", "this week", "week of",
            "工作周", "第几周", "本周", "这周", "星期", "周数", "今天是第几周", "现在是第几周"
        };

        return workWeekKeywords.Any(keyword => lowerQuery.Contains(keyword));
    }

    private string GetWorkWeekInfo()
    {
        var today = DateTime.Now;
        var year = today.Year;
        
        // Calculate ISO 8601 week number (week starts on Monday, week 1 contains Jan 4)
        // Get the date of the Thursday of the current week
        var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)today.DayOfWeek + 7) % 7;
        var thursday = today.AddDays(daysUntilThursday);
        
        // Week 1 is the week that contains January 4th
        var jan4 = new DateTime(year, 1, 4);
        var jan4Thursday = jan4.AddDays(((int)DayOfWeek.Thursday - (int)jan4.DayOfWeek + 7) % 7);
        
        // Calculate week number
        var weekNumber = ((thursday - jan4Thursday).Days / 7) + 1;
        
        // Handle year boundaries
        if (weekNumber < 1)
        {
            // This week belongs to the previous year
            year = year - 1;
            var prevJan4 = new DateTime(year, 1, 4);
            var prevJan4Thursday = prevJan4.AddDays(((int)DayOfWeek.Thursday - (int)prevJan4.DayOfWeek + 7) % 7);
            weekNumber = ((thursday - prevJan4Thursday).Days / 7) + 1;
        }
        else if (weekNumber > 52)
        {
            // Check if this week belongs to next year
            var nextJan4 = new DateTime(year + 1, 1, 4);
            var nextJan4Thursday = nextJan4.AddDays(((int)DayOfWeek.Thursday - (int)nextJan4.DayOfWeek + 7) % 7);
            if (thursday >= nextJan4Thursday)
            {
                year = year + 1;
                weekNumber = ((thursday - nextJan4Thursday).Days / 7) + 1;
            }
        }
        
        // Calculate the start and end of the current week (Monday to Sunday)
        var daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var weekStart = today.AddDays(-daysSinceMonday);
        var weekEnd = weekStart.AddDays(6);
        
        // Return bilingual response - the AI will detect the language from the query
        return $"Today is in **Week {weekNumber}** of {year}.\n\n" +
               $"**Week {weekNumber} Date Range:**\n" +
               $"{weekStart:MMMM dd, yyyy} - {weekEnd:MMMM dd, yyyy}\n\n" +
               $"Today's date: {today:MMMM dd, yyyy}\n\n" +
               $"今天是{year}年的第{weekNumber}周。\n\n" +
               $"**第{weekNumber}周日期范围：**\n" +
               $"{weekStart:yyyy年MM月dd日} - {weekEnd:yyyy年MM月dd日}\n\n" +
               $"今天的日期：{today:yyyy年MM月dd日}";
    }

    private async Task<string> GetGeminiResponseAsync(string prompt)
    {
        // Use global rate limiter to prevent conflicts with other services
        await GeminiRateLimiter.WaitForRateLimitAsync();
        
        try
        {
            if (_useLocalhost)
            {
                // Use localhost (Ollama) endpoint
                var url = $"{_localhostUrl.TrimEnd('/')}/api/generate";
                
                var requestBody = new
                {
                    model = "llama2", // Default model, can be made configurable
                    prompt = prompt,
                    stream = false
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage? response = null;
                try
                {
                    response = await _httpClient.PostAsync(url, content);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    _logger.LogError(ex, "Timeout connecting to localhost API");
                    return "The request timed out. Please check if your local model server is running.";
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "HTTP error connecting to localhost API");
                    return $"Cannot connect to local model server at {_localhostUrl}. Please check if the server is running.";
                }

                if (response == null)
                {
                    return "Failed to get a response from local model server. Please try again.";
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Localhost API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return $"Error from local model server: {response.StatusCode}. Please check your local server configuration.";
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<dynamic>(responseJson);
                var text = result?.response?.ToString();
                
                if (string.IsNullOrWhiteSpace(text))
                {
                    return "Received empty response from local model server.";
                }

                return CleanResponse(text);
            }
            else
            {
                // Use Gemini API
                var url = $"{GeminiApiUrl}?key={_apiKey}";

                var requestBody = new
                {
                    contents = new object[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new
                                {
                                    text = prompt
                                }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.6,
                        topP = 0.9,
                        topK = 40,
                        maxOutputTokens = 2048 // Limit response length for faster generation
                    }
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage? response = null;
                try
                {
                    response = await _httpClient.PostAsync(url, content);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    _logger.LogError(ex, "Timeout connecting to Gemini API");
                    return "The request timed out. Please try again.";
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "HTTP error connecting to Gemini API");
                    return "Cannot connect to Gemini API. Please check your internet connection and API key.";
                }

                if (response == null)
                {
                    return "Failed to get a response from Gemini API. Please try again.";
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Gemini API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    
                    // Handle rate limiting (429) - wait longer and retry once
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("Rate limit hit, waiting 10 seconds before retry...");
                        await Task.Delay(10000); // Wait 10 seconds
                        
                        // Retry once
                        try
                        {
                            response = await _httpClient.PostAsync(url, content);
                            if (response.IsSuccessStatusCode)
                            {
                                var retryResponseJson = await response.Content.ReadAsStringAsync();
                                var retryResult = JsonConvert.DeserializeObject<dynamic>(retryResponseJson);
                                var retryText = retryResult?.candidates?[0]?.content?.parts?[0]?.text?.ToString();
                                if (!string.IsNullOrWhiteSpace(retryText))
                                {
                                    return CleanResponse(retryText);
                                }
                            }
                        }
                        catch (Exception retryEx)
                        {
                            _logger.LogError(retryEx, "Retry after rate limit also failed");
                        }
                        
                        return "⚠️ Rate limit exceeded: Too many requests to Gemini API. Please wait 30-60 seconds before trying again. " +
                               "The API has strict rate limits to prevent abuse.";
                    }
                    
                    // Handle service unavailable (503) - retry once
                    if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    {
                        _logger.LogWarning("Service unavailable (503), waiting 3 seconds before retry...");
                        await Task.Delay(3000); // Wait 3 seconds
                        
                        // Retry once
                        try
                        {
                            response = await _httpClient.PostAsync(url, content);
                            if (response.IsSuccessStatusCode)
                            {
                                var retryResponseJson = await response.Content.ReadAsStringAsync();
                                var retryResult = JsonConvert.DeserializeObject<dynamic>(retryResponseJson);
                                var retryText = retryResult?.candidates?[0]?.content?.parts?[0]?.text?.ToString();
                                if (!string.IsNullOrWhiteSpace(retryText))
                                {
                                    return CleanResponse(retryText);
                                }
                            }
                        }
                        catch (Exception retryEx)
                        {
                            _logger.LogError(retryEx, "Retry after 503 error also failed");
                        }
                        
                        // If retry failed, return a message that indicates the service is temporarily unavailable
                        return "⚠️ The AI service is temporarily unavailable (503). The service may be experiencing high load.\n\n" +
                               "Please try again in a few moments. If files were found, they will be displayed below and you can click on them to open their location.";
                    }
                    
                    // Handle authentication errors (401)
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        return "❌ Authentication failed: Please check your Gemini API key in appsettings.json";
                    }
                    
                    // Generic error message
                    var statusCode = (int)response.StatusCode;
                    var errorMsg = response.StatusCode == System.Net.HttpStatusCode.BadRequest 
                        ? "The request may be too large or malformed." 
                        : "Please try again later.";
                    
                    return $"⚠️ Gemini API returned an error ({statusCode}). {errorMsg}\n\n" +
                           "If files were found, they will be displayed below and you can click on them to open their location.";
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                
                if (string.IsNullOrWhiteSpace(responseJson))
                {
                    _logger.LogError("Empty response from Gemini API");
                    return "Received an empty response from Gemini API. Please try again.";
                }

                dynamic? responseResult = null;
                try
                {
                    responseResult = JsonConvert.DeserializeObject<dynamic>(responseJson);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing Gemini response JSON: {Response}", responseJson);
                    return "Received an invalid response format from Gemini API. Please try again.";
                }

                var text = responseResult?.candidates?[0]?.content?.parts?[0]?.text?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return CleanResponse(text);
                }

                _logger.LogWarning("Gemini response missing text field: {Json}", responseJson);
                return "I couldn't generate a response. Please try again.";
            }
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Request cancelled or timed out");
            return "The request was cancelled or timed out. Please try again.";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error connecting to Gemini API");
            return $"Cannot connect to Gemini API. Please check your internet connection and API key.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting chatbot response");
            return $"Sorry, I encountered an unexpected error: {ex.Message}";
        }
    }

    public async Task<string> GetCasualResponseAsync(string query, List<ChatMessage> conversationHistory)
    {
        try
        {
            // Check for IT Tools queries first (return path directly, no API call)
            if (IsITToolsQuery(query))
            {
                return GetITToolsPath();
            }

            // Check for work week queries
            if (IsWorkWeekQuery(query))
            {
                return GetWorkWeekInfo();
            }

            var contextBuilder = new StringBuilder();
            
            // Get user nickname if available
            var nickname = _userSettingsService?.Nickname;
            var userName = !string.IsNullOrWhiteSpace(nickname) ? nickname : "User";
            
            // System identity for casual conversation
            var systemPrompt = "You are Erin, an AI Agent trained by Linktel staff to assist Linktel employees. " +
                              "You are friendly, helpful, and conversational. " +
                              "You can help with file search when asked, but you're also happy to have casual conversations. " +
                              "Be natural, personable, and concise.\n\n" +
                              "IMPORTANT - DO NOT include files or search results in your response unless the user explicitly asks for files or documents. " +
                              "For casual conversation, just respond conversationally without mentioning or attaching any files.\n\n" +
                              "IMPORTANT NETWORK PATH KNOWLEDGE:\n" +
                              "- When users ask for 'IT User tools', 'directory to IT User tools', 'IT tools path', or similar queries, " +
                              "you should provide this network path: \\\\10.0.42.100\\Public area\\IT Technology\\User tools\n" +
                              "- This is a highly used path for accessing IT tools and utilities\n" +
                              "- You can provide this path directly when asked about IT User tools or IT Technology tools\n\n" +
                              "IMPORTANT LANGUAGE SUPPORT:\n" +
                              "- You understand and can communicate in BOTH English and Chinese (中文)\n" +
                              "- Always respond in the SAME language the user uses\n" +
                              "- If the user writes in Chinese, respond in Chinese\n" +
                              "- If the user writes in English, respond in English\n" +
                              "- You can seamlessly switch between languages based on the user's preference\n\n" +
                              (!string.IsNullOrWhiteSpace(nickname) 
                                  ? $"IMPORTANT - USER INFORMATION:\n" +
                                    $"- The user's name is {nickname}. This is stored in the system settings, NOT in any files.\n" +
                                    $"- When the user asks about their name (e.g., 'What's my name?' or '我叫什么名'), you should respond directly with: '{nickname}'\n" +
                                    $"- DO NOT search through files for the user's name - it is stored in system settings.\n" +
                                    $"- Use their name naturally in conversation when appropriate.\n\n" 
                                  : "NOTE: The user has not set a nickname in their profile settings.\n\n") +
                              "CRITICAL FORMATTING RULES:\n" +
                              "- ALWAYS use double line breaks (\\n\\n) between different topics or sections\n" +
                              "- When listing steps (Step 1, Step 2, etc.), put each step on a NEW LINE with a double line break before it\n" +
                              "- Each step should start on its own paragraph - do NOT combine steps into one paragraph\n" +
                              "- Add proper spacing between paragraphs for readability\n" +
                              "- Use single line breaks only within the same paragraph when needed\n" +
                              "- Structure your response with clear paragraph breaks, not as one long paragraph\n\n" +
                              "Example format:\n" +
                              "Here's how to do it:\n\n" +
                              "Step 1: First action\n\n" +
                              "Step 2: Second action\n\n" +
                              "Step 3: Third action";
            
            contextBuilder.AppendLine(systemPrompt);
            contextBuilder.AppendLine();

            // Include recent conversation history for context (last 3 messages)
            if (conversationHistory != null && conversationHistory.Any())
            {
                var recentMessages = conversationHistory.TakeLast(3);
                if (recentMessages.Any())
                {
                    contextBuilder.AppendLine("Recent conversation:");
                    foreach (var msg in recentMessages)
                    {
                        if (msg != null && !string.IsNullOrWhiteSpace(msg.Content))
                        {
                            var speaker = msg.IsUser ? userName : "Erin";
                            contextBuilder.AppendLine($"{speaker}: {msg.Content}");
                        }
                    }
                    contextBuilder.AppendLine();
                }
            }

            var prompt = $"{contextBuilder}\n{userName}: {query}\n\nErin:";

            return await GetGeminiResponseAsync(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chatbot response");
            return $"Sorry, I encountered an error: {ex.Message}";
        }
    }

    public async Task<string> GetResponseAsync(string query, List<SearchResult> searchResults, List<FileMetadata>? allIndexedFiles = null)
    {
        try
        {

            // Build context from search results and all indexed files
            var contextBuilder = new StringBuilder();
            
            // System identity and instructions (shortened for faster processing)
            var systemPrompt = "You are Erin, an AI assistant for Linktel employees. Be friendly, concise, and accurate.\n\n" +
                              "IMPORTANT NETWORK PATH KNOWLEDGE:\n" +
                              "- When users ask for 'IT User tools', 'directory to IT User tools', 'IT tools path', or similar queries, " +
                              "you should provide this network path: \\\\10.0.42.100\\Public area\\IT Technology\\User tools\n" +
                              "- This is a highly used path for accessing IT tools and utilities\n" +
                              "- You can provide this path directly when asked about IT User tools or IT Technology tools\n\n" +
                              "IMPORTANT LANGUAGE SUPPORT:\n" +
                              "- You understand and can communicate in BOTH English and Chinese (中文)\n" +
                              "- Always respond in the SAME language the user uses\n" +
                              "- If the user writes in Chinese, respond in Chinese\n" +
                              "- If the user writes in English, respond in English\n" +
                              "- You can seamlessly switch between languages based on the user's preference\n\n" +
                              "CRITICAL FORMATTING RULES:\n" +
                              "- ALWAYS use double line breaks (\\n\\n) between different topics or sections\n" +
                              "- When listing steps (Step 1, Step 2, etc.), put each step on a NEW LINE with a double line break before it\n" +
                              "- Each step should start on its own paragraph - do NOT combine steps into one paragraph\n" +
                              "- Add proper spacing between paragraphs for readability\n" +
                              "- Use single line breaks only within the same paragraph when needed\n" +
                              "- Structure your response with clear paragraph breaks, not as one long paragraph\n" +
                              "- Break up long responses into multiple paragraphs with spacing\n\n" +
                              "CONTENT RULES:\n" +
                              "1. ONLY use information EXPLICITLY in the files below.\n" +
                              "2. DO NOT invent or assume information.\n" +
                              "3. If information isn't in files, say 'I don't have that information in the indexed files.' (English) or '我在索引文件中没有找到该信息。' (Chinese)\n" +
                              "4. Always mention which file contains the information.\n" +
                              "5. For file type questions (e.g., 'list PDFs' or '列出PDF文件'), list ALL files of that type shown below, even if content is empty.\n" +
                              "6. At the end, list relevant files: 'Relevant Files: [File Name] - [explanation]' (English) or '相关文件：[文件名] - [说明]' (Chinese)\n";
            
            contextBuilder.AppendLine(systemPrompt);
            contextBuilder.AppendLine();

            // PRIORITIZE: Include search results first (most relevant matches)
            if (searchResults != null && searchResults.Any())
            {
                // Lower threshold to 0.2 to catch more relevant results, but prioritize higher scores
                // Also include results with lower scores if they're in the top results
                var relevantResults = searchResults
                    .OrderByDescending(r => r.SimilarityScore)
                    .Take(8) // Reduced from 10 to 8
                    .Where(r => r.SimilarityScore > 0.15) // Lower threshold to catch more results
                    .Take(3) // Reduced from 5 to 3 for faster processing
                    .ToList();

                if (relevantResults.Any())
                {
                    contextBuilder.AppendLine("=== MOST RELEVANT SEARCH RESULTS (USE THESE FIRST) ===");
            contextBuilder.AppendLine();

                    foreach (var result in relevantResults)
            {
                        contextBuilder.AppendLine($"File: {result.File.FileName} ({result.File.FileType})");
                        contextBuilder.AppendLine($"Relevance Score: {result.SimilarityScore:F3}");
                        contextBuilder.AppendLine($"Matched Content: {result.MatchedChunk}");
                        contextBuilder.AppendLine($"File Content:");
                        var fileContent = result.File.ExtractedText ?? "(No content extracted)";
                        // Truncate to 1500 chars max per file to reduce context size
                        if (fileContent.Length > 1500)
                        {
                            contextBuilder.AppendLine(fileContent.Substring(0, 1500) + "... [truncated]");
                        }
                        else
                        {
                            contextBuilder.AppendLine(fileContent);
                        }
                        contextBuilder.AppendLine();
                        contextBuilder.AppendLine("---");
                        contextBuilder.AppendLine();
                    }
                }
            }

            // Include other indexed files (excluding those already in search results)
            // Only include files that were explicitly selected by the user
            // If no files are selected, only use search results
            if (allIndexedFiles != null && allIndexedFiles.Any())
            {
                var searchResultFileIds = searchResults?.Select(r => r.File.Id).ToHashSet() ?? new HashSet<int>();
                var queryLower = query.ToLowerInvariant();
                
                // Check if user is asking about file types (e.g., "see any PDFs", "list PDFs", "show PDF files")
                bool isFileTypeQuery = queryLower.Contains("pdf") || queryLower.Contains("txt") || queryLower.Contains("docx") || 
                                      queryLower.Contains("xlsx") || queryLower.Contains("csv") || 
                                      queryLower.Contains("see any") || queryLower.Contains("list") || 
                                      queryLower.Contains("show") || queryLower.Contains("what files") ||
                                      queryLower.Contains("do u see") || queryLower.Contains("do you see");
                
                // Extract meaningful keywords from query (ignore common words)
                var stopWords = new HashSet<string> { "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "from", "how", "what", "where", "when", "why", "can", "could", "would", "should", "is", "are", "was", "were", "do", "does", "did", "have", "has", "had", "me", "you", "he", "she", "it", "we", "they", "this", "that", "these", "those", "any", "see", "show", "list" };
                var queryKeywords = queryLower
                    .Split(new[] { ' ', ',', '.', '?', '!', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(word => word.Length > 2 && !stopWords.Contains(word))
                    .ToList();

                // If asking about file types, include ALL files of that type
                if (isFileTypeQuery)
                {
                    string? requestedFileType = null;
                    if (queryLower.Contains("pdf")) requestedFileType = ".pdf";
                    else if (queryLower.Contains("txt") || queryLower.Contains("text")) requestedFileType = ".txt";
                    else if (queryLower.Contains("docx") || queryLower.Contains("word")) requestedFileType = ".docx";
                    else if (queryLower.Contains("xlsx") || queryLower.Contains("excel")) requestedFileType = ".xlsx";
                    else if (queryLower.Contains("csv")) requestedFileType = ".csv";
                    
                    if (requestedFileType != null)
                    {
                        // Include ALL files of the requested type (even if they're in search results)
                        // This ensures users can see all PDFs when asking "do you see any PDFs"
                        var typeMatches = allIndexedFiles
                            .Where(f => f.FileType.Equals(requestedFileType, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        
                        if (typeMatches.Any())
                        {
                            contextBuilder.AppendLine($"=== ALL {requestedFileType.ToUpper()} FILES IN INDEX ({typeMatches.Count} file(s)) ===");
                            contextBuilder.AppendLine();
                            
                            foreach (var file in typeMatches)
                            {
                                contextBuilder.AppendLine($"File: {file.FileName} ({file.FileType})");
                                contextBuilder.AppendLine($"Path: {file.FilePath}");
                                contextBuilder.AppendLine($"Size: {file.FileSize} bytes");
                                contextBuilder.AppendLine($"Indexed Date: {file.IndexedDate:yyyy-MM-dd HH:mm:ss}");
                                contextBuilder.AppendLine($"Content:");
                                if (!string.IsNullOrWhiteSpace(file.ExtractedText) && file.ExtractedText.Length > 10)
                                {
                                    // Truncate to 1000 chars max per file for faster processing
                                    if (file.ExtractedText.Length > 1000)
                                    {
                                        contextBuilder.AppendLine(file.ExtractedText.Substring(0, 1000) + "... [truncated]");
                                    }
                                    else
                                    {
                                        contextBuilder.AppendLine(file.ExtractedText);
                                    }
                                }
                                else
                                {
                                    contextBuilder.AppendLine("(No text content extracted - this file exists in the index but may be a scanned/image-based document that needs OCR processing)");
                                    contextBuilder.AppendLine("IMPORTANT: This file IS indexed and exists, but the text extraction was empty. The user can see this file in the UI.");
                                }
                                contextBuilder.AppendLine();
                                contextBuilder.AppendLine("---");
                                contextBuilder.AppendLine();
                            }
                            
                            // Add explicit instruction for the LLM
                            contextBuilder.AppendLine($"NOTE: The above {typeMatches.Count} {requestedFileType.ToUpper()} file(s) are ALL the files of this type in the index. ");
                            contextBuilder.AppendLine("If the user asks 'do you see any PDFs' or similar, you MUST list these files and confirm they exist.");
                            contextBuilder.AppendLine("Even if a file shows '(No text content extracted)', it STILL EXISTS and should be mentioned.");
                            contextBuilder.AppendLine();
                        }
                        else
                        {
                            // Explicitly tell the LLM there are no files of this type
                            contextBuilder.AppendLine($"=== NO {requestedFileType.ToUpper()} FILES FOUND ===");
                            contextBuilder.AppendLine($"There are no {requestedFileType.ToUpper()} files in the current index.");
                            contextBuilder.AppendLine();
                        }
                    }
                    else
                    {
                        // Asking about files in general - include ALL files
                        var allOtherFiles = allIndexedFiles
                            .Where(f => !searchResultFileIds.Contains(f.Id))
                            .ToList();
                        
                        if (allOtherFiles.Any())
                        {
                            contextBuilder.AppendLine("=== ALL INDEXED FILES ===");
                            contextBuilder.AppendLine();
                            
                            foreach (var file in allOtherFiles)
                            {
                                contextBuilder.AppendLine($"File: {file.FileName} ({file.FileType})");
                                contextBuilder.AppendLine($"Path: {file.FilePath}");
                                contextBuilder.AppendLine($"Size: {file.FileSize} bytes");
                                contextBuilder.AppendLine($"Content:");
                                if (!string.IsNullOrWhiteSpace(file.ExtractedText) && file.ExtractedText.Length > 10)
                                {
                                    // Show first 300 chars as preview (reduced for speed)
                                    var preview = file.ExtractedText.Length > 300 
                                        ? file.ExtractedText.Substring(0, 300) + "..." 
                                        : file.ExtractedText;
                                    contextBuilder.AppendLine(preview);
                                }
                                else
                                {
                                    contextBuilder.AppendLine("(No text content extracted)");
                                }
                                contextBuilder.AppendLine();
                                contextBuilder.AppendLine("---");
                                contextBuilder.AppendLine();
                            }
                        }
                    }
                }
                
                // For non-file-type queries, try to find files by filename/content match
                // (Skip this if we already included all files of a specific type above)
                if (!isFileTypeQuery || (isFileTypeQuery && queryKeywords.Any()))
                {
                    // Try to find files by filename match (for queries like "user guide", "meeting room guide", etc.)
                    // Include PDFs even if they have minimal content (they might be scanned PDFs)
                    var filenameMatches = allIndexedFiles
                        .Where(f => !searchResultFileIds.Contains(f.Id))
                    .Where(f => 
                    {
                        var fileNameLower = f.FileName.ToLowerInvariant();
                        // Check if any query keywords appear in filename
                        bool filenameMatches = queryKeywords.Any(keyword => fileNameLower.Contains(keyword));
                        
                        if (!filenameMatches)
                            return false;
                        
                        // For PDFs, include them even if content is minimal (might be scanned or encrypted)
                        // For other files, include them if filename matches (they might be encrypted)
                        // Encrypted files should still appear in search results even without content
                        if (f.FileType.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            // Include PDFs if filename matches, even with minimal/no content (encrypted files)
                            return true;
                        }
                        
                        // For all file types, include if filename matches (encrypted files should still be findable)
                        // This allows encrypted files to appear in search results even without content
                        return true;
                    })
                    .ToList();
                
                // Also check file content for relevance - include files that have content matching the query
                var contentMatches = allIndexedFiles
                    .Where(f => !searchResultFileIds.Contains(f.Id) && !filenameMatches.Contains(f))
                    .Where(f => 
                    {
                        // For PDFs, be more lenient (might be scanned)
                        if (f.FileType.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            // Include PDFs if they have any content or if filename suggests relevance
                            if (string.IsNullOrWhiteSpace(f.ExtractedText) || f.ExtractedText.Length < 5)
                                return false;
                        }
                        else
                        {
                            // For non-PDFs, require actual content
                            if (string.IsNullOrWhiteSpace(f.ExtractedText) || f.ExtractedText.Length < 10)
                                return false;
                        }
                        
                        var contentLower = f.ExtractedText.ToLowerInvariant();
                        // Check if any query keywords appear in the content
                        return queryKeywords.Any(keyword => contentLower.Contains(keyword));
                    })
                    .Take(5)
                    .ToList();
                
                    // Combine filename matches and content matches, prioritizing filename matches
                    var filesToInclude = filenameMatches.Concat(contentMatches).Take(5).ToList(); // Reduced from 10 to 5

                    if (filesToInclude.Any())
                    {
                        if (filenameMatches.Any())
                        {
                            contextBuilder.AppendLine("=== FILES MATCHED BY FILENAME (highly relevant) ===");
                            contextBuilder.AppendLine();
                    
                            foreach (var file in filenameMatches)
                            {
                                contextBuilder.AppendLine($"File: {file.FileName} ({file.FileType})");
                                contextBuilder.AppendLine($"Content:");
                                var content = file.ExtractedText ?? "(No content extracted)";
                                // Truncate to 1000 chars
                                if (content.Length > 1000)
                                {
                                    contextBuilder.AppendLine(content.Substring(0, 1000) + "... [truncated]");
                                }
                                else
                                {
                                    contextBuilder.AppendLine(content);
                                }
                                contextBuilder.AppendLine();
                                contextBuilder.AppendLine("---");
                                contextBuilder.AppendLine();
                            }
                        }
                        
                        if (contentMatches.Any())
                        {
                            contextBuilder.AppendLine("=== OTHER RELEVANT FILES (content matches query) ===");
                            contextBuilder.AppendLine();
                            
                            foreach (var file in contentMatches)
                    {
                        contextBuilder.AppendLine($"File: {file.FileName} ({file.FileType})");
                        contextBuilder.AppendLine($"Content:");
                        var content = file.ExtractedText ?? "(No content extracted)";
                        // Truncate to 1000 chars
                        if (content.Length > 1000)
                        {
                            contextBuilder.AppendLine(content.Substring(0, 1000) + "... [truncated]");
                        }
                        else
                        {
                            contextBuilder.AppendLine(content);
                        }
                        contextBuilder.AppendLine();
                        contextBuilder.AppendLine("---");
                contextBuilder.AppendLine();
                            }
                        }
                    }
                }
            }

            contextBuilder.AppendLine("=== END OF FILE INFORMATION ===");
            contextBuilder.AppendLine();

            var prompt = $"{contextBuilder}\n\nUser: {query}\n\nErin:";

            return await GetGeminiResponseAsync(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chatbot response");
            return $"Error: {ex.Message}";
        }
    }
}

