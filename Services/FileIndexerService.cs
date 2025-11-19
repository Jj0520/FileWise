using System.IO;
using FileWise.Models;
using FileWise.Utilities;
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
        try
        {
            statusCallback?.Invoke("Scanning folder for files...");
            _logger.LogInformation("Starting to index folder: {FolderPath}", folderPath);
            Console.WriteLine($"📁 Starting to index folder: {folderPath}");
            
            // Run the expensive folder scan on a background thread so the UI can continue rendering
            var files = await Task.Run(() => GetSupportedFiles(folderPath));
        var totalFiles = files.Count;
        var processedFiles = 0;

            _logger.LogInformation("Found {FileCount} files to index in {FolderPath}", totalFiles, folderPath);
            Console.WriteLine($"📊 Found {totalFiles} files to index");
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
            
            _logger.LogInformation("Completed indexing folder: {FolderPath}, processed {ProcessedFiles}/{TotalFiles} files", 
                folderPath, processedFiles, totalFiles);
            Console.WriteLine($"✅ Completed indexing: {processedFiles}/{totalFiles} files processed");
            statusCallback?.Invoke($"Indexed {processedFiles}/{totalFiles} files");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in IndexFolderAsync for {FolderPath}", folderPath);
            Console.WriteLine($"❌ Error indexing folder {folderPath}: {ex.Message}");
            statusCallback?.Invoke($"Error: {ex.Message}");
            throw;
        }
    }

    public async Task IndexFilesAsync(List<string> filePaths, Action<double>? progressCallback = null, Action<string>? statusCallback = null)
    {
        if (filePaths == null || !filePaths.Any())
        {
            statusCallback?.Invoke("No files to index");
            return;
        }

        var totalFiles = filePaths.Count;
        _processedFiles = 0; // Reset counter

        statusCallback?.Invoke($"Indexing {totalFiles} file(s)...");

        // Separate PDFs from other files
        var pdfFiles = filePaths.Where(f => Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
        var otherFiles = filePaths.Where(f => !Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();

        // Process PDFs with lower concurrency
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
        statusCallback?.Invoke($"Indexed {totalFiles} file(s)");
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
            // Check if it's a network path
            bool isNetworkPath = folderPath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase);
            
            _logger.LogInformation("Scanning folder: {FolderPath} (Network: {IsNetwork})", folderPath, isNetworkPath);
            Console.WriteLine($"🔍 Scanning folder: {folderPath} (Network: {isNetworkPath})");
            
            if (isNetworkPath)
            {
                // For network paths, use recursive directory traversal with better error handling
                _logger.LogInformation("Using recursive scan for network path");
                Console.WriteLine("🌐 Using recursive scan for network path");
                GetSupportedFilesRecursive(folderPath, files, isNetworkPath);
            }
            else
            {
                // For local paths, use the faster Directory.GetFiles
                _logger.LogInformation("Using fast Directory.GetFiles for local path");
                Console.WriteLine("💾 Using fast scan for local path");
                foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(file).ToLower();
                if (SupportedExtensions.Contains(extension))
                {
                    files.Add(file);
                }
            }
            }
            
            _logger.LogInformation("Found {FileCount} supported files in {FolderPath}", files.Count, folderPath);
            Console.WriteLine($"✅ Found {files.Count} supported files");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning folder: {FolderPath}", folderPath);
            Console.WriteLine($"❌ Error scanning folder: {ex.Message}");
        }
        return files;
    }

    private void GetSupportedFilesRecursive(string folderPath, List<string> files, bool isNetworkPath, int depth = 0)
    {
        // Prevent infinite recursion and skip system folders
        if (depth > 100) 
        {
            _logger.LogWarning("Max depth reached for: {FolderPath}", folderPath);
            return;
        }
        
        // Skip system folders that commonly cause issues on network shares
        var folderName = Path.GetFileName(folderPath)?.ToLower() ?? "";
        if (folderName == "#recycle" || 
            folderName == "$recycle.bin" || 
            folderName == "system volume information" ||
            folderName == "recycler" ||
            folderName.StartsWith("$"))
        {
            _logger.LogInformation("Skipping system folder: {FolderPath}", folderPath);
            Console.WriteLine($"⏭️ Skipping system folder: {folderName}");
            return;
        }
        
        if (depth == 0)
        {
            Console.WriteLine($"📂 Starting recursive scan from: {folderPath}");
        }

        try
        {
            // Get files in current directory
            string[] currentFiles;
            try
            {
                currentFiles = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Access denied to folder: {FolderPath}", folderPath);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogWarning("Directory not found: {FolderPath}", folderPath);
                return;
            }
            catch (IOException ex)
            {
                // Network errors - log but continue
                _logger.LogWarning(ex, "Network error accessing folder: {FolderPath}", folderPath);
                return;
            }

            foreach (var file in currentFiles)
            {
                try
                {
                    var extension = Path.GetExtension(file).ToLower();
                    if (SupportedExtensions.Contains(extension))
                    {
                        files.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing file: {FilePath}", file);
                }
            }

            // Recursively process subdirectories
            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(folderPath);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Access denied to subdirectories in: {FolderPath}", folderPath);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Network error accessing subdirectories in: {FolderPath}", folderPath);
                return;
            }

            foreach (var subdirectory in subdirectories)
            {
                try
                {
                    GetSupportedFilesRecursive(subdirectory, files, isNetworkPath, depth + 1);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing subdirectory: {Subdirectory}", subdirectory);
                    // Continue with other subdirectories
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in recursive file scan: {FolderPath}", folderPath);
        }
    }

    private async Task ProcessFileAsync(string filePath, bool forceReindex = false)
    {
        try
        {
            // For network files, add retry logic
            FileInfo? fileInfo = null;
            int retries = 3;
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    fileInfo = new FileInfo(filePath);
                    // Try to access a property to ensure file is accessible
                    _ = fileInfo.Length;
                    break; // Success, exit retry loop
                }
                catch (IOException ex) when (i < retries - 1 && filePath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(ex, "Network error accessing file (attempt {Attempt}/{Retries}): {FilePath}", i + 1, retries, filePath);
                    await Task.Delay(1000 * (i + 1)); // Exponential backoff: 1s, 2s, 3s
                }
                catch (UnauthorizedAccessException)
                {
                    _logger.LogWarning("Access denied to file: {FilePath}", filePath);
                    return;
                }
            }

            if (fileInfo == null)
            {
                _logger.LogError("Failed to access file after {Retries} attempts: {FilePath}", retries, filePath);
                return;
            }

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
                // Check encryption type to provide more specific messages
                bool isEfsEncrypted = FileEncryptionHelper.IsEfsEncrypted(filePath);
                bool isPdfPasswordEncrypted = fileExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) 
                    && FileEncryptionHelper.IsPdfPasswordEncrypted(filePath);
                bool isWpsEncrypted = FileEncryptionHelper.IsWpsOfficeEncrypted(filePath);
                bool canReadFile = FileEncryptionHelper.CanReadFile(filePath);
                
                string warningMsg;
                
                // Handle WPS Office encryption (most specific case first)
                if (isWpsEncrypted)
                {
                    if (isEfsEncrypted && isPdfPasswordEncrypted)
                    {
                        warningMsg = $"🔒 WPS Office / Kingsoft Office Encrypted file (also has EFS + PDF password): {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                                   $"WPS Office uses proprietary encryption that prevents content extraction. " +
                                   $"Solution: Open in WPS Office, remove encryption/password, and re-export as a standard PDF. " +
                                   $"File metadata indexed but no content extracted. File will still appear in search results by filename.";
                    }
                    else if (isEfsEncrypted)
                    {
                        warningMsg = $"🔒 WPS Office / Kingsoft Office Encrypted file (also has EFS): {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                                   $"WPS Office uses proprietary encryption that prevents content extraction. " +
                                   $"EFS decryption works automatically, but WPS encryption still blocks content. " +
                                   $"Solution: Open in WPS Office, remove encryption, and re-export as a standard PDF. " +
                                   $"File metadata indexed but no content extracted.";
                    }
                    else if (isPdfPasswordEncrypted)
                    {
                        warningMsg = $"🔒 WPS Office / Kingsoft Office Encrypted file (also has PDF password): {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                                   $"WPS Office uses proprietary encryption that prevents content extraction. " +
                                   $"Solution: Open in WPS Office, remove encryption/password, and re-export as a standard PDF. " +
                                   $"File metadata indexed but no content extracted. File will still appear in search results by filename.";
                    }
                    else
                    {
                        warningMsg = $"🔒 WPS Office / Kingsoft Office Encrypted file: {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                                   $"WPS Office uses proprietary encryption that prevents content extraction. " +
                                   $"Solution: Open in WPS Office, remove encryption, and re-export as a standard PDF. " +
                                   $"File metadata indexed but no content extracted. File will still appear in search results by filename.";
                            }
                        }
                else if (isEfsEncrypted && isPdfPasswordEncrypted)
                    {
                    // Both EFS and PDF password encryption
                    if (canReadFile)
                    {
                        warningMsg = $"🔒 File has Windows EFS + PDF Password Encryption: {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                                   $"EFS decryption works automatically, but PDF password encryption prevents content extraction. " +
                                   $"File metadata indexed but no content extracted. File will still appear in search results by filename.";
                    }
                    else
                    {
                        warningMsg = $"🔒 File has Windows EFS + PDF Password Encryption: {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                                   $"Cannot read file - may need to run under the same user account that encrypted it. " +
                                   $"File metadata indexed but no content extracted.";
                    }
                }
                else if (isEfsEncrypted)
                {
                    // EFS only - should work automatically
                    if (canReadFile)
                    {
                        warningMsg = $"🔒 Windows EFS-encrypted file: {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                                   $"EFS decryption works automatically under the same user account. " +
                                   $"No text extracted - file may be empty, corrupted, or in an unsupported format. " +
                                   $"File metadata indexed but no embeddings created. Extracted text length: {text?.Length ?? 0}";
                    }
                    else
                    {
                        warningMsg = $"🔒 Windows EFS-encrypted file: {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                                   $"Cannot read file - ensure the application is running under the same user account that encrypted the file. " +
                                   $"File metadata indexed but no content extracted.";
                    }
                }
                else if (isPdfPasswordEncrypted)
                {
                    // PDF password encryption only
                    warningMsg = $"🔒 PDF Password-encrypted file: {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                               $"PDF password encryption prevents content extraction without the password. " +
                               $"File metadata indexed but no content extracted. File will still appear in search results by filename.";
                }
                else
                {
                    // No encryption detected, but no text extracted
                    warningMsg = $"No text extracted from: {filePath} (FileType: {fileInfo.Extension}, Size: {fileInfo.Length} bytes). " +
                               $"File metadata indexed but no embeddings created. " +
                               $"Extracted text length: {text?.Length ?? 0}";
                }
                
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





