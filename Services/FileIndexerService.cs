using System.IO;
using FileWise.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FileWise.Services;

public class FileIndexerService : IFileIndexerService
{
    private readonly IDatabaseService _databaseService;
    private readonly IEmbeddingService _embeddingService;
    private readonly TextExtractorService _textExtractor;
    private readonly ILogger<FileIndexerService> _logger;
    private readonly int _chunkSize;
    private readonly int _maxConcurrentFiles;
    private readonly int _maxConcurrentPdfs; // PDF-specific concurrency limit
    private int _processedFiles = 0; // Shared counter for progress tracking
    private static readonly string[] SupportedExtensions = { ".txt", ".pdf", ".docx", ".xlsx", ".csv" };

    public FileIndexerService(
        IDatabaseService databaseService,
        IEmbeddingService embeddingService,
        TextExtractorService textExtractor,
        IConfiguration configuration,
        ILogger<FileIndexerService> logger)
    {
        _databaseService = databaseService;
        _embeddingService = embeddingService;
        _textExtractor = textExtractor;
        _logger = logger;
        _chunkSize = configuration.GetValue<int>("Indexing:ChunkSize", 1000);
        _maxConcurrentFiles = configuration.GetValue<int>("Indexing:MaxConcurrentFiles", 50);
        _maxConcurrentPdfs = configuration.GetValue<int>("Indexing:MaxConcurrentPdfs", 2); // Default to 2 to avoid rate limits
    }

    public async Task IndexFolderAsync(string folderPath, Action<double>? progressCallback = null, Action<string>? statusCallback = null)
    {
        var files = GetSupportedFiles(folderPath);
        var totalFiles = files.Count;
        var processedFiles = 0;

        statusCallback?.Invoke($"Found {totalFiles} files to index");

        // Separate PDFs from other files - PDFs are slower and need more time
        var pdfFiles = files.Where(f => Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
        var otherFiles = files.Where(f => !Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();

        // Process PDFs with lower concurrency (they're slower and use API calls)
        // Use the configured MaxConcurrentPdfs to avoid rate limits
        var pdfSemaphore = new SemaphoreSlim(_maxConcurrentPdfs);
        // Process other files with normal concurrency
        var otherSemaphore = new SemaphoreSlim(_maxConcurrentFiles);

        var allTasks = new List<Task>();

        // Process PDFs
        foreach (var file in pdfFiles)
        {
            allTasks.Add(ProcessFileWithSemaphoreAsync(file, pdfSemaphore, totalFiles, progressCallback, statusCallback));
        }

        // Process other files
        foreach (var file in otherFiles)
        {
            allTasks.Add(ProcessFileWithSemaphoreAsync(file, otherSemaphore, totalFiles, progressCallback, statusCallback));
        }

        await Task.WhenAll(allTasks);
    }

    private async Task ProcessFileWithSemaphoreAsync(
        string file, 
        SemaphoreSlim semaphore, 
        int totalFiles,
        Action<double>? progressCallback,
        Action<string>? statusCallback)
    {
        await semaphore.WaitAsync();
        try
        {
            // Add delay for PDFs to avoid rate limits (1 second between PDF processing)
            if (Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(1000); // Wait 1 second before processing each PDF
            }
            
            await ProcessFileAsync(file);
            var currentProcessed = Interlocked.Increment(ref _processedFiles);
            var progress = (double)currentProcessed / totalFiles * 100;
            progressCallback?.Invoke(progress);
            statusCallback?.Invoke($"Processed {currentProcessed}/{totalFiles} files");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file: {FilePath}", file);
            statusCallback?.Invoke($"Error processing {Path.GetFileName(file)}: {ex.Message}");
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task IndexFileAsync(string filePath, Action<string>? statusCallback = null, bool forceReindex = false)
    {
        if (!File.Exists(filePath))
        {
            statusCallback?.Invoke($"File not found: {filePath}");
            return;
        }

        var extension = Path.GetExtension(filePath).ToLower();
        if (!SupportedExtensions.Contains(extension))
        {
            statusCallback?.Invoke($"File type not supported: {extension}");
            return;
        }

        statusCallback?.Invoke($"Indexing {Path.GetFileName(filePath)}...");
        await ProcessFileAsync(filePath, forceReindex);
        statusCallback?.Invoke($"Successfully indexed {Path.GetFileName(filePath)}");
    }

    private List<string> GetSupportedFiles(string folderPath)
    {
        var files = new List<string>();
        try
        {
            foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(file).ToLower();
                if (SupportedExtensions.Contains(extension))
                {
                    files.Add(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning folder: {FolderPath}", folderPath);
        }
        return files;
    }

    private async Task ProcessFileAsync(string filePath, bool forceReindex = false)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var hash = _textExtractor.ComputeFileHash(filePath);

            // Check if file already indexed with same hash (skip if forceReindex is false)
            if (!forceReindex && await _databaseService.FileExistsAsync(filePath, hash))
            {
                _logger.LogInformation("File already indexed: {FilePath}", filePath);
                return;
            }

            // Force reindex: delete old embeddings first
            if (forceReindex)
            {
                var existingForReindex = await _databaseService.GetFileMetadataByPathAsync(filePath);
                if (existingForReindex != null)
                {
                    await _databaseService.DeleteEmbeddingsByFileIdAsync(existingForReindex.Id);
                    _logger.LogInformation("Force re-indexing: Deleted old embeddings for {FilePath}", filePath);
                    Console.WriteLine($"  🔄 Force re-indexing: Deleted old embeddings");
                }
            }

            Console.WriteLine($"  📖 Extracting text from {Path.GetFileName(filePath)}...");
            var text = await _textExtractor.ExtractTextAsync(filePath);

            // Normalize file extension to lowercase with dot (e.g., ".pdf")
            var fileExtension = fileInfo.Extension;
            if (!string.IsNullOrEmpty(fileExtension))
            {
                fileExtension = fileExtension.ToLowerInvariant();
                if (!fileExtension.StartsWith("."))
                {
                    fileExtension = "." + fileExtension;
                }
            }

            var metadata = new FileMetadata
            {
                FileName = fileInfo.Name,
                FilePath = filePath,
                FileType = fileExtension,
                FileSize = fileInfo.Length,
                ModifiedDate = fileInfo.LastWriteTime,
                ExtractedText = text ?? string.Empty,
                IndexedDate = DateTime.Now,
                Hash = hash
            };

            // Check if file exists but hash changed
            var existing = await _databaseService.GetFileMetadataByPathAsync(filePath);
            if (existing != null)
            {
                metadata.Id = existing.Id;
                await _databaseService.UpdateFileMetadataAsync(metadata);
                // Delete old embeddings if file was updated
                await _databaseService.DeleteEmbeddingsByFileIdAsync(metadata.Id);
            }
            else
            {
                metadata.Id = await _databaseService.InsertFileMetadataAsync(metadata);
            }

            // Only create embeddings if we have text content
            if (!string.IsNullOrWhiteSpace(text) && text.Length > 10)
            {
                // Log successful extraction
                _logger.LogInformation("Successfully extracted {TextLength} characters from {FilePath} (FileType: {FileType})", 
                    text.Length, filePath, fileInfo.Extension);
                
                // Chunk text and create embeddings
                var chunks = _textExtractor.ChunkText(text, _chunkSize);
                _logger.LogInformation("Created {ChunkCount} chunks for {FilePath}", chunks.Count, filePath);
                
                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];
                    try
                    {
                        var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk);
                        
                        var embeddingVector = new EmbeddingVector
                        {
                            FileMetadataId = metadata.Id,
                            ChunkText = chunk,
                            ChunkIndex = i,
                            EmbeddingJson = System.Text.Json.JsonSerializer.Serialize(embedding),
                            CreatedDate = DateTime.Now
                        };

                        await _databaseService.InsertEmbeddingAsync(embeddingVector);
                    }
                    catch (Exception embEx)
                    {
                        _logger.LogError(embEx, "Error creating embedding for chunk {ChunkIndex} of {FilePath}", i, filePath);
                        // Continue with other chunks
                    }
                }
                
                _logger.LogInformation("Successfully created embeddings for {FilePath}", filePath);
            }
            else
            {
                // Check if file might be encrypted
                bool mightBeEncrypted = false;
                if (fileExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                        {
                            var buffer = new byte[Math.Min(1024, (int)fileInfo.Length)];
                            var bytesRead = fs.Read(buffer, 0, buffer.Length);
                            var bufferStr = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead).ToLower();
                            
                            if (bufferStr.Contains("encrypt") || bufferStr.Contains("password") || 
                                bufferStr.Contains("security") || bufferStr.Contains("/encrypt"))
                            {
                                mightBeEncrypted = true;
                            }
                        }
                    }
                    catch
                    {
                        // If we can't read, might be encrypted
                        mightBeEncrypted = true;
                    }
                }
                
                var warningMsg = mightBeEncrypted
                    ? $"🔒 Encrypted file detected: {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                      $"File metadata indexed but no content extracted. File will still appear in search results by filename."
                    : $"No text extracted from: {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                               $"File metadata indexed but no embeddings created. " +
                               $"Extracted text length: {text?.Length ?? 0}";
                _logger.LogWarning(warningMsg);
                System.Diagnostics.Debug.WriteLine(warningMsg);
                Console.WriteLine($"⚠️ {warningMsg}");
            }

            _logger.LogInformation("Indexed file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file: {FilePath}", filePath);
            throw;
        }
    }
}

