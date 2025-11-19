using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileWise.Models;
using FileWise.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace FileWise.ViewModels;

public enum ViewMode
{
    Icons,
    List
}

public enum SortBy
{
    Name,
    Type,
    Size,
    ModifiedDate
}

public partial class MainViewModel : ViewModelBase
{
    private readonly IFileIndexerService _fileIndexerService;
    private readonly IChatbotService _chatbotService;
    private readonly IVectorSearchService _vectorSearchService;
    private readonly IDatabaseService _databaseService;
    private readonly UserSettingsService _userSettingsService;
    private readonly ChatHistoryService _chatHistoryService;
    private readonly TextExtractorService _textExtractorService;

    [ObservableProperty]
    private string _selectedFolder = string.Empty;

    [ObservableProperty]
    private bool _isIndexing;

    [ObservableProperty]
    private double _indexingProgress;

    [ObservableProperty]
    private string _indexingStatus = LocalizationService.Instance.GetString("Status_Ready");

    [ObservableProperty]
    private string _userQuery = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ChatTab> _chatTabs = new();

    [ObservableProperty]
    private ChatTab? _selectedChatTab;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _chatMessages = new();

    [ObservableProperty]
    private ObservableCollection<SearchResult> _searchResults = new();

    [ObservableProperty]
    private bool _isProcessingQuery;

    [ObservableProperty]
    private SearchResult? _selectedSearchResult;

    [ObservableProperty]
    private bool _isChatPanelOpen = false;

    [ObservableProperty]
    private bool _isPrimarySidebarVisible = true;

    [ObservableProperty]
    private ViewMode _currentViewMode = ViewMode.Icons;

    [ObservableProperty]
    private SortBy _currentSortBy = SortBy.Name;

    [ObservableProperty]
    private bool _sortAscending = true;

    [ObservableProperty]
    private ObservableCollection<FileMetadata> _indexedFiles = new();

    [ObservableProperty]
    private FileMetadata? _selectedFile;

    [ObservableProperty]
    private bool _needsReindexing = false;

    [ObservableProperty]
    private ObservableCollection<FileMetadata> _selectedFilesForContext = new();

    [ObservableProperty]
    private bool _isSelectionMode = false;

    [ObservableProperty]
    private ObservableCollection<FileMetadata> _selectedFilesForReindex = new();

    [ObservableProperty]
    private ObservableCollection<string> _recentFolders = new();

    private CollectionViewSource _filesViewSource = new();

    public ICollectionView FilesView => _filesViewSource.View;

    public MainViewModel(
        IFileIndexerService fileIndexerService,
        IChatbotService chatbotService,
        IVectorSearchService vectorSearchService,
        IDatabaseService databaseService,
        UserSettingsService userSettingsService,
        ChatHistoryService chatHistoryService,
        TextExtractorService textExtractorService)
    {
        _fileIndexerService = fileIndexerService;
        _chatbotService = chatbotService;
        _vectorSearchService = vectorSearchService;
        _databaseService = databaseService;
        _userSettingsService = userSettingsService;
        _chatHistoryService = chatHistoryService;
        _textExtractorService = textExtractorService;

        _filesViewSource.Source = IndexedFiles;
        _filesViewSource.View.Filter = FilterFiles;
        _filesViewSource.View.SortDescriptions.Add(new SortDescription("FileName", ListSortDirection.Ascending));

        // Load saved folder path
        SelectedFolder = _userSettingsService.SelectedFolder;

        // Load recent folders
        LoadRecentFolders();

        // Load chat tabs
        LoadChatTabs();

        // Load indexed files on startup
        _ = LoadIndexedFilesAsync();
        
        // Initialize status
        IndexingStatus = LocalizationService.Instance.GetString("Status_Ready");
        IndexingProgress = 0;
    }

    partial void OnCurrentSortByChanged(SortBy value)
    {
        ApplySorting();
    }

    partial void OnSortAscendingChanged(bool value)
    {
        ApplySorting();
    }

    partial void OnSearchQueryChanged(string value)
    {
        _filesViewSource.View.Refresh();
    }

    partial void OnSelectedFolderChanged(string value)
    {
        // Reset progress when folder changes
        IndexingProgress = 0;
        IndexingStatus = LocalizationService.Instance.GetString("Status_CheckingFiles");
        
        // Reload files when folder changes to show only files from the new directory
        _ = LoadIndexedFilesAsync();
        _ = CheckReindexingNeededAsync();
        
        // Automatically start indexing when a folder is selected
        if (!string.IsNullOrEmpty(value) && Directory.Exists(value))
        {
            // Add to recent folders
            _userSettingsService.AddRecentFolder(value);
            LoadRecentFolders();
            _ = AutoIndexFolderAsync();
        }
        else
        {
            IndexingStatus = LocalizationService.Instance.GetString("Status_NoFolderSelected");
        }
    }

    private async Task AutoIndexFolderAsync()
    {
        if (string.IsNullOrEmpty(SelectedFolder) || !Directory.Exists(SelectedFolder))
        {
            IndexingProgress = 0;
            IndexingStatus = LocalizationService.Instance.GetString("Status_NoFolderSelected");
            return;
        }

        // Check if indexing is already in progress
        if (IsIndexing)
            return;

        // Check if there are files that need indexing
        var supportedExtensions = new[] { ".txt", ".pdf", ".docx", ".xlsx", ".csv" };
        
        // Check if it's a network path
        bool isNetworkPath = SelectedFolder.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase);
        
        // For network paths, we need to handle them differently
        List<string> filesInDirectory;
        try
        {
            if (isNetworkPath)
            {
                // For network paths, use a safer approach - let the indexer handle it
                // We'll just check if the folder exists and start indexing
                if (!Directory.Exists(SelectedFolder))
                {
                    IndexingStatus = "Network folder not accessible";
                    return;
                }
                // Don't pre-scan network folders - let the indexer do it with proper error handling
                filesInDirectory = new List<string>(); // Will be populated by the indexer
            }
            else
            {
                filesInDirectory = Directory.GetFiles(SelectedFolder, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error scanning folder: {ex.Message}");
            IndexingStatus = $"Error scanning folder: {ex.Message}";
            return;
        }
        
        if (isNetworkPath)
        {
            // For network paths, just start indexing - let the indexer handle file discovery
            IsIndexing = true;
            IndexingProgress = 0;
            IndexingStatus = "Scanning network folder...";
            
            try
            {
                await _fileIndexerService.IndexFolderAsync(SelectedFolder, 
                    progress => 
                    {
                        IndexingProgress = progress;
                        System.Diagnostics.Debug.WriteLine($"Progress: {progress}%");
                    },
                    status => 
                    {
                        IndexingStatus = status;
                        System.Diagnostics.Debug.WriteLine($"Status: {status}");
                    });
                
                IndexingProgress = 100;
                IndexingStatus = LocalizationService.Instance.GetString("Status_IndexingCompleted");
                await LoadIndexedFilesAsync();
                await CheckReindexingNeededAsync();
            }
            catch (Exception ex)
            {
                IndexingStatus = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Auto-indexing error: {ex.Message}");
                Console.WriteLine($"❌ Auto-indexing error: {ex.Message}");
            }
            finally
            {
                IsIndexing = false;
            }
        }
        else
        {
            // For local paths, use the existing logic
        if (!filesInDirectory.Any())
        {
            IndexingProgress = 100;
                IndexingStatus = LocalizationService.Instance.GetString("Status_NoFilesToIndex");
            return;
        }

        // Check if files need indexing
        var indexedFiles = await _databaseService.GetAllFilesAsync();
        var indexedFilesInDirectory = indexedFiles
            .Where(f => IsFileInDirectory(f.FilePath, SelectedFolder))
            .ToDictionary(f => f.FilePath, f => f);

        var filesNeedingIndex = new List<string>();
        foreach (var filePath in filesInDirectory)
        {
            if (!indexedFilesInDirectory.ContainsKey(filePath))
            {
                filesNeedingIndex.Add(filePath);
            }
            else
            {
                var indexedFile = indexedFilesInDirectory[filePath];
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.LastWriteTime > indexedFile.IndexedDate || 
                    fileInfo.Length != indexedFile.FileSize)
                {
                    filesNeedingIndex.Add(filePath);
                }
            }
        }

        // If all files are already indexed, show 100%
        if (!filesNeedingIndex.Any())
        {
            IndexingProgress = 100;
                IndexingStatus = string.Format(LocalizationService.Instance.GetString("Status_AllFilesIndexed"), filesInDirectory.Count);
            return;
        }

        // Auto-index only new/modified files
        IsIndexing = true;
        IndexingProgress = 0;
            IndexingStatus = string.Format(LocalizationService.Instance.GetString("Status_AutoIndexing"), filesNeedingIndex.Count);

        try
        {
            // Only index files that need indexing (new or modified)
            if (filesNeedingIndex.Any())
            {
                await _fileIndexerService.IndexFilesAsync(filesNeedingIndex, 
                    progress => IndexingProgress = progress,
                    status => IndexingStatus = status);
            }
            
            IndexingProgress = 100;
                IndexingStatus = string.Format(LocalizationService.Instance.GetString("Status_AllFilesIndexed"), filesInDirectory.Count);
            await LoadIndexedFilesAsync();
            await CheckReindexingNeededAsync();
        }
        catch (Exception ex)
        {
            IndexingStatus = $"Auto-indexing error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Auto-indexing error: {ex.Message}");
        }
        finally
        {
            IsIndexing = false;
            }
        }
    }

    private bool FilterFiles(object item)
    {
        if (item is not FileMetadata file)
            return false;

        // First, ensure the file is in the selected folder
        if (!string.IsNullOrEmpty(SelectedFolder) && Directory.Exists(SelectedFolder))
        {
            if (!IsFileInDirectory(file.FilePath, SelectedFolder))
            {
                return false; // File is not in the selected folder
            }
        }

        // If search query is empty, show all files (that are in the selected folder)
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return true;

        var query = SearchQuery.Trim().ToLowerInvariant();

        // Search by file name (case-insensitive)
        var fileNameMatch = file.FileName.ToLowerInvariant().Contains(query);

        // Search by file extension/type (case-insensitive)
        var fileTypeMatch = file.FileType.ToLowerInvariant().Contains(query);

        // Return true if either matches
        return fileNameMatch || fileTypeMatch;
    }

    private void ApplySorting()
    {
        _filesViewSource.View.SortDescriptions.Clear();
        string propertyName = CurrentSortBy switch
        {
            SortBy.Name => "FileName",
            SortBy.Type => "FileType",
            SortBy.Size => "FileSize",
            SortBy.ModifiedDate => "ModifiedDate",
            _ => "FileName"
        };
        _filesViewSource.View.SortDescriptions.Add(
            new SortDescription(propertyName, SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending));
    }

    private string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        
        try
        {
            // Get full path and normalize separators
            var normalized = Path.GetFullPath(path);
            // Normalize to use consistent directory separators
            normalized = normalized.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            // Remove trailing separators
            normalized = normalized.TrimEnd(Path.DirectorySeparatorChar);
            return normalized;
        }
        catch
        {
            // If GetFullPath fails, try basic normalization
            var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            normalized = normalized.TrimEnd(Path.DirectorySeparatorChar);
            return normalized;
        }
    }

    private bool IsFileInDirectory(string filePath, string directoryPath)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(directoryPath))
            return false;

        try
        {
            var normalizedFile = NormalizePath(filePath);
            var normalizedDir = NormalizePath(directoryPath);
            
            // Get the directory of the file
            var fileDirectory = Path.GetDirectoryName(normalizedFile);
            if (string.IsNullOrEmpty(fileDirectory))
                return false;

            // Normalize the file's directory
            fileDirectory = NormalizePath(fileDirectory);
            
            // Check if file is directly in the selected folder or in a subdirectory
            // Files directly in folder: fileDirectory equals normalizedDir
            // Files in subdirectories: fileDirectory starts with normalizedDir + separator
            return fileDirectory.Equals(normalizedDir, StringComparison.OrdinalIgnoreCase) ||
                   fileDirectory.StartsWith(normalizedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking if file is in directory: {ex.Message}");
            return false;
        }
    }

    private async Task LoadIndexedFilesAsync()
    {
        try
        {
            var files = await _databaseService.GetAllFilesAsync();
            IndexedFiles.Clear();
            
            // DEBUG: Log all file types
            System.Diagnostics.Debug.WriteLine($"=== DEBUG: Loading {files.Count} files from database ===");
            var pdfFiles = files.Where(f => f.FileType.ToLowerInvariant().Contains("pdf")).ToList();
            System.Diagnostics.Debug.WriteLine($"PDF files found: {pdfFiles.Count}");
            foreach (var pdf in pdfFiles)
            {
                System.Diagnostics.Debug.WriteLine($"  - {pdf.FileName}: FileType='{pdf.FileType}' (Length={pdf.FileType.Length})");
            }
            Console.WriteLine($"=== DEBUG: Loading {files.Count} files, {pdfFiles.Count} PDFs ===");
            
            // Filter files by selected folder if a folder is selected
            if (!string.IsNullOrEmpty(SelectedFolder) && Directory.Exists(SelectedFolder))
            {
                foreach (var file in files)
                {
                    // Check if file is in the selected folder (including subdirectories)
                    if (IsFileInDirectory(file.FilePath, SelectedFolder))
                    {
                        // Only add files that still exist on disk
                        if (File.Exists(file.FilePath))
                        {
                            IndexedFiles.Add(file);
                            // DEBUG: Log PDF files being added
                            if (file.FileType.ToLowerInvariant().Contains("pdf"))
                            {
                                System.Diagnostics.Debug.WriteLine($"Adding PDF to UI: {file.FileName}, FileType='{file.FileType}'");
                                Console.WriteLine($"Adding PDF to UI: {file.FileName}, FileType='{file.FileType}'");
                            }
                        }
                        else
                        {
                            // File was deleted - remove from database to keep it clean
                            System.Diagnostics.Debug.WriteLine($"File no longer exists on disk, removing from database: {file.FilePath}");
                            try
                            {
                                await _databaseService.DeleteFileMetadataAsync(file.Id);
                            }
                            catch (Exception deleteEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error deleting file metadata: {deleteEx.Message}");
                            }
                        }
                    }
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"=== DEBUG: Added {IndexedFiles.Count} files to UI, PDFs: {IndexedFiles.Count(f => f.FileType.ToLowerInvariant().Contains("pdf"))} ===");
            Console.WriteLine($"=== DEBUG: Added {IndexedFiles.Count} files to UI ===");
            
            ApplySorting();
            _filesViewSource.View.Refresh();
            
            // Check if reindexing is needed after loading files
            await CheckReindexingNeededAsync();
        }
        catch (Exception ex)
        {
            // Log error silently or show to user
            System.Diagnostics.Debug.WriteLine($"Error loading indexed files: {ex.Message}");
        }
    }

    private async Task CheckReindexingNeededAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(SelectedFolder) || !Directory.Exists(SelectedFolder))
            {
                NeedsReindexing = false;
                return;
            }

            var supportedExtensions = new[] { ".txt", ".pdf", ".docx", ".xlsx", ".csv" };
            var filesInDirectory = Directory.GetFiles(SelectedFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            if (!filesInDirectory.Any())
            {
                NeedsReindexing = false;
                return;
            }

            var indexedFiles = await _databaseService.GetAllFilesAsync();
            var indexedFilesInDirectory = indexedFiles
                .Where(f => IsFileInDirectory(f.FilePath, SelectedFolder))
                .ToDictionary(f => f.FilePath, f => f);

            bool needsReindex = false;

            foreach (var filePath in filesInDirectory)
            {
                if (!indexedFilesInDirectory.ContainsKey(filePath))
                {
                    needsReindex = true;
                    break;
                }

                var indexedFile = indexedFilesInDirectory[filePath];
                var fileInfo = new FileInfo(filePath);

                if (fileInfo.LastWriteTime > indexedFile.IndexedDate)
                {
                    var currentHash = _textExtractorService.ComputeFileHash(filePath);
                    if (currentHash != indexedFile.Hash)
                    {
                        needsReindex = true;
                        break;
                    }
                }
            }

            NeedsReindexing = needsReindex;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking reindexing status: {ex.Message}");
            NeedsReindexing = false;
        }
    }

    [RelayCommand]
    private async Task ReindexFiles()
    {
        await IndexFiles();
    }

    [RelayCommand]
    private void ToggleSelectionMode()
    {
        IsSelectionMode = !IsSelectionMode;
        if (!IsSelectionMode)
        {
            // Clear selection when exiting selection mode
            foreach (var file in IndexedFiles)
            {
                file.IsSelectedForReindex = false;
            }
            SelectedFilesForReindex.Clear();
        }
    }

    [RelayCommand]
    private async Task ReindexSelectedFiles()
    {
        var filesToReindex = SelectedFilesForReindex.ToList();
        if (filesToReindex.Count == 0)
            return;

        try
        {
            IsIndexing = true;
            IndexingProgress = 0;
            var totalFiles = filesToReindex.Count;
            var processed = 0;

            foreach (var file in filesToReindex)
            {
                if (string.IsNullOrEmpty(file.FilePath) || !File.Exists(file.FilePath))
                {
                    processed++;
                    continue;
                }

                IndexingStatus = string.Format(LocalizationService.Instance.GetString("Status_ReIndexing"), file.FileName, processed + 1, totalFiles);
                Console.WriteLine($"Re-indexing file: {file.FileName}");
                System.Diagnostics.Debug.WriteLine($"Re-indexing file: {file.FileName}");

                await _fileIndexerService.IndexFileAsync(file.FilePath,
                    status => IndexingStatus = status,
                    forceReindex: true);

                processed++;
                IndexingProgress = (double)processed / totalFiles * 100;
            }

            // Reload files to show updated content
            await LoadIndexedFilesAsync();

            // Clear selection
            foreach (var file in filesToReindex)
            {
                file.IsSelectedForReindex = false;
            }
            SelectedFilesForReindex.Clear();
            IsSelectionMode = false;

            IndexingStatus = string.Format(LocalizationService.Instance.GetString("Status_ReIndexed"), processed);
            MessageBox.Show($"Successfully re-indexed {processed} file(s).", "Re-indexing Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error re-indexing files: {ex.Message}");
            Console.WriteLine($"Error re-indexing files: {ex.Message}");
            MessageBox.Show($"Error re-indexing files: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsIndexing = false;
            IndexingProgress = 0;
        }
    }

    [RelayCommand]
    private async Task ReindexSingleFile(FileMetadata? file)
    {
        if (file == null || string.IsNullOrEmpty(file.FilePath))
            return;

        if (!File.Exists(file.FilePath))
        {
            MessageBox.Show($"File not found: {file.FilePath}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            IndexingStatus = string.Format(LocalizationService.Instance.GetString("Status_ReIndexingSingle"), file.FileName);
            Console.WriteLine($"Re-indexing file: {file.FileName}");
            System.Diagnostics.Debug.WriteLine($"Re-indexing file: {file.FileName}");
            await _fileIndexerService.IndexFileAsync(file.FilePath, 
                status => IndexingStatus = status, 
                forceReindex: true); // Force re-extraction even if hash matches
            
            // Reload files to show updated content
            await LoadIndexedFilesAsync();
            
            // Verify the file was reindexed with content
            var reloadedFile = IndexedFiles.FirstOrDefault(f => f.FilePath == file.FilePath);
            if (reloadedFile != null)
            {
                var contentLength = reloadedFile.ExtractedText?.Length ?? 0;
                System.Diagnostics.Debug.WriteLine($"✓ Re-indexed file {file.FileName}: {contentLength} characters extracted");
                Console.WriteLine($"✓ Re-indexed file {file.FileName}: {contentLength} characters extracted");
                
                if (contentLength > 0)
                {
                    IndexingStatus = string.Format(LocalizationService.Instance.GetString("Status_SuccessfullyReIndexed"), file.FileName, contentLength);
                    MessageBox.Show($"Successfully re-indexed {file.FileName}\n\nExtracted {contentLength} characters.", "Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    IndexingStatus = string.Format(LocalizationService.Instance.GetString("Status_ReIndexedNoText"), file.FileName);
                    MessageBox.Show($"Re-indexed {file.FileName}, but no text was extracted.\n\nThis may be a scanned/image-based PDF that needs OCR processing.", "Warning", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                IndexingStatus = string.Format(LocalizationService.Instance.GetString("Status_SuccessfullyReIndexedSimple"), file.FileName);
                MessageBox.Show($"Successfully re-indexed {file.FileName}", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            IndexingStatus = $"Error: {ex.Message}";
            MessageBox.Show($"Error re-indexing file: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task SelectFolder()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog();
        // Set initial directory to previously selected folder if available
        if (!string.IsNullOrEmpty(SelectedFolder) && Directory.Exists(SelectedFolder))
        {
            dialog.SelectedPath = SelectedFolder;
        }
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SelectedFolder = dialog.SelectedPath;
            // Save the selected folder
            _userSettingsService.SelectedFolder = SelectedFolder;
        }
    }

    [RelayCommand]
    private async Task IndexFiles()
    {
        if (string.IsNullOrEmpty(SelectedFolder))
        {
            MessageBox.Show("Please select a folder first.", "No Folder Selected", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsIndexing = true;
        IndexingProgress = 0;
        IndexingStatus = LocalizationService.Instance.GetString("Status_StartingIndexing");

        try
        {
            await _fileIndexerService.IndexFolderAsync(SelectedFolder, 
                progress => IndexingProgress = progress,
                status => IndexingStatus = status);
            
            IndexingStatus = LocalizationService.Instance.GetString("Status_IndexingCompleted");
            await LoadIndexedFilesAsync();
            await CheckReindexingNeededAsync();
            MessageBox.Show("Files indexed successfully!", "Success", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            IndexingStatus = $"Error: {ex.Message}";
            MessageBox.Show($"Error indexing files: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsIndexing = false;
        }
    }

    [RelayCommand]
    private void ToggleChatPanel()
    {
        IsChatPanelOpen = !IsChatPanelOpen;
    }

    [RelayCommand]
    private void TogglePrimarySidebar()
    {
        IsPrimarySidebarVisible = !IsPrimarySidebarVisible;
    }

    [RelayCommand]
    private void SelectRecentFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        SelectedFolder = folderPath;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null)
            {
                // Use reflection to call OpenSettingsWindow method
                var method = mainWindow.GetType().GetMethod("OpenSettingsWindow");
                method?.Invoke(mainWindow, null);
            }
        });
    }

    private void LoadRecentFolders()
    {
        RecentFolders.Clear();
        var recentFolders = _userSettingsService.RecentFolders;
        foreach (var folder in recentFolders)
        {
            if (Directory.Exists(folder))
            {
                RecentFolders.Add(folder);
            }
        }
    }

    [RelayCommand]
    private void ToggleFileSelection(FileMetadata? file)
    {
        if (file == null) return;
        
        file.IsSelectedForContext = !file.IsSelectedForContext;
        
        if (file.IsSelectedForContext)
        {
            if (!SelectedFilesForContext.Contains(file))
            {
                SelectedFilesForContext.Add(file);
            }
        }
        else
        {
            SelectedFilesForContext.Remove(file);
        }
        
        System.Diagnostics.Debug.WriteLine($"File {file.FileName} selection toggled: {file.IsSelectedForContext}");
    }

    [RelayCommand]
    private void SwitchToIconsView()
    {
        CurrentViewMode = ViewMode.Icons;
    }

    [RelayCommand]
    private void SwitchToListView()
    {
        CurrentViewMode = ViewMode.List;
    }

    [RelayCommand]
    private void SortByName()
    {
        if (CurrentSortBy == SortBy.Name)
        {
            // Toggle between A-Z (ascending) and Z-A (descending)
            SortAscending = !SortAscending;
        }
        else
        {
            CurrentSortBy = SortBy.Name;
            SortAscending = true; // Start with A-Z
        }
    }

    [RelayCommand]
    private void SortByType()
    {
        // Just sort by type, no alternating
            CurrentSortBy = SortBy.Type;
        SortAscending = true; // Default to ascending
    }

    [RelayCommand]
    private void SortBySize()
    {
        if (CurrentSortBy == SortBy.Size)
        {
            // Toggle between small-to-big (ascending) and big-to-small (descending)
            SortAscending = !SortAscending;
        }
        else
        {
            CurrentSortBy = SortBy.Size;
            SortAscending = true; // Start with small-to-big
        }
    }

    [RelayCommand]
    private void SortByDate()
    {
        if (CurrentSortBy == SortBy.ModifiedDate)
        {
            // Toggle between earliest-to-oldest (ascending) and oldest-to-earliest (descending)
            SortAscending = !SortAscending;
        }
        else
        {
            CurrentSortBy = SortBy.ModifiedDate;
            SortAscending = true; // Start with earliest-to-oldest
        }
    }

    [RelayCommand]
    private async Task SendQuery()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(UserQuery))
                return;

            // Ensure we have a selected tab
            if (SelectedChatTab == null)
            {
                CreateNewTab();
            }

            // Double-check after CreateNewTab
            if (SelectedChatTab == null)
            {
                System.Diagnostics.Debug.WriteLine("Failed to create chat tab");
                return;
            }

            // Ensure Messages list is initialized
            if (SelectedChatTab.Messages == null)
            {
                SelectedChatTab.Messages = new List<ChatMessage>();
            }

            var userMessage = new ChatMessage
            {
                Content = UserQuery ?? string.Empty,
                IsUser = true,
                Timestamp = DateTime.Now
            };

            try
            {
                // Ensure UI thread for collection updates
                if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        SelectedChatTab.Messages.Add(userMessage);
                        ChatMessages.Add(userMessage);
                    });
                }
                else
                {
                    SelectedChatTab.Messages.Add(userMessage);
                    ChatMessages.Add(userMessage);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding user message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return;
            }
            
            var query = UserQuery;
            UserQuery = string.Empty;
            IsProcessingQuery = true;

            try
            {
                // Determine if this is a file search query or casual conversation
                bool isSearchQuery = false;
                try
                {
                    isSearchQuery = _chatbotService.IsFileSearchQuery(query);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error determining query type: {ex.Message}");
                    // Default to casual conversation if we can't determine
                    isSearchQuery = false;
                }

                string response = string.Empty;
                List<SearchResult> searchResults = new();

                // Check for IT Tools query first - return path directly without API calls
                var lowerQuery = query.ToLower();
                var itToolsKeywords = new[] { "it tools", "it user tools", "it technology tools", "user tools",
                    "directory to it tools", "it tools path", "it tools directory",
                    "where is it tools", "it tools location", "it tools folder",
                    "new user setup", "user setup", "setup", "new user", "user setup tools",
                    "IT工具", "IT用户工具", "IT技术工具", "用户工具", "IT工具路径", "IT工具目录",
                    "IT工具在哪里", "IT工具位置", "IT工具文件夹", "新用户设置", "用户设置", "设置" };
                
                if (itToolsKeywords.Any(keyword => lowerQuery.Contains(keyword)))
                {
                    response = "The IT User tools directory is located at:\n\n" +
                              "\\\\10.0.42.100\\Public area\\IT Technology\\User tools\n\n" +
                              "You can access this network path directly to find IT tools and utilities.";
                    
                    // Add user message
                    if (SelectedChatTab != null)
                    {
                        SelectedChatTab.Messages.Add(new ChatMessage
                        {
                            Content = query,
                            IsUser = true,
                            Timestamp = DateTime.Now
                        });
                    }

                    // Add AI response
                    if (SelectedChatTab != null)
                    {
                        SelectedChatTab.Messages.Add(new ChatMessage
                        {
                            Content = response,
                            IsUser = false,
                            Timestamp = DateTime.Now
                        });
                    }

                    SaveCurrentTab();
                    UpdateChatMessages();
                    IsProcessingQuery = false;
                    return;
                }

                try
                {
                    if (isSearchQuery)
                    {
                        // Perform vector search for file-related queries
                        try
                        {
                            var allSearchResults = await _vectorSearchService.SearchAsync(query, topK: 10);
                            
                            // Filter search results to only include files from the current directory
                            if (!string.IsNullOrEmpty(SelectedFolder) && Directory.Exists(SelectedFolder))
                            {
                                searchResults = allSearchResults
                                    .Where(r => IsFileInDirectory(r.File.FilePath, SelectedFolder))
                                    .Take(5)
                                    .ToList();
                            }
                            else
                            {
                                searchResults = allSearchResults.Take(5).ToList();
                            }
                            
                        // Ensure UI thread for collection updates
                        Action updateSearchResults = () =>
                        {
                            try
                            {
                                SearchResults.Clear();
                                foreach (var result in searchResults)
                                {
                                    SearchResults.Add(result);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error updating search results: {ex.Message}");
                            }
                        };

                        if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(updateSearchResults);
                        }
                        else
                        {
                            updateSearchResults();
                        }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error performing vector search: {ex.Message}");
                            // Continue without search results
                            searchResults = new List<SearchResult>();
                        }

                        // Get chatbot response with search results and selected files for context
                        // ONLY use explicitly selected files - if none are selected, pass empty list
                        var filesForContext = SelectedFilesForContext.ToList();
                        
                        // DEBUG: Log what files are being passed to the chatbot
                        System.Diagnostics.Debug.WriteLine($"=== DEBUG: Passing {filesForContext.Count} files to chatbot (explicitly selected only) ===");
                        Console.WriteLine($"=== DEBUG: Passing {filesForContext.Count} files to chatbot ===");
                        if (filesForContext.Any())
                        {
                            foreach (var f in filesForContext)
                            {
                                var contentLength = f.ExtractedText?.Length ?? 0;
                                System.Diagnostics.Debug.WriteLine($"  - {f.FileName} ({f.FileType}): {contentLength} chars");
                                Console.WriteLine($"  - {f.FileName} ({f.FileType}): {contentLength} chars");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("⚠️ No files selected for context - chatbot will only use search results");
                            Console.WriteLine("⚠️ No files selected for context - chatbot will only use search results");
                        }
                        System.Diagnostics.Debug.WriteLine($"Query: {query}");
                        Console.WriteLine($"Query: {query}");
                        
                        response = await _chatbotService.GetResponseAsync(query, searchResults, filesForContext);
                    }
                    else
                    {
                        // Handle casual conversation without searching
                        // Clear any previous search results for casual conversations
                        try
                        {
                            if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    SearchResults.Clear();
                                });
                            }
                            else
                            {
                                SearchResults.Clear();
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error clearing search results: {ex.Message}");
                        }
                        
                        // Pass conversation history excluding the current user message (which we just added)
                        List<ChatMessage> history = new List<ChatMessage>();
                        try
                        {
                            if (SelectedChatTab?.Messages != null && SelectedChatTab.Messages.Count > 1)
                            {
                                history = SelectedChatTab.Messages.Take(SelectedChatTab.Messages.Count - 1).ToList();
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error getting conversation history: {ex.Message}");
                            history = new List<ChatMessage>();
                        }
                        response = await _chatbotService.GetCasualResponseAsync(query, history);
                    }
                }
                catch (Exception apiEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting chatbot response: {apiEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"Stack trace: {apiEx.StackTrace}");
                    
                    // Deduplicate search results before checking
                    var uniqueFilesFromError = searchResults
                        .GroupBy(r => r.File.FilePath, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.OrderByDescending(r => r.SimilarityScore).First())
                        .ToList();
                    
                    // If we have search results, provide a more helpful message
                    if (uniqueFilesFromError.Any())
                    {
                        response = $"⚠️ I encountered an error generating a response: {apiEx.Message}\n\n" +
                                  $"However, I found {uniqueFilesFromError.Count} relevant file(s) for you below. " +
                                  $"You can click on them to open their location in File Explorer.";
                    }
                    else
                    {
                        response = $"Sorry, I encountered an error while processing your request: {apiEx.Message}";
                    }
                }

                // Deduplicate search results by file path (same file might appear multiple times from different chunks)
                var uniqueFiles = searchResults
                    .GroupBy(r => r.File.FilePath, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(r => r.SimilarityScore).First()) // Take the highest scoring match for each file
                    .ToList();

                if (string.IsNullOrWhiteSpace(response))
                {
                    // If we have files but no response, provide a helpful message
                    if (uniqueFiles.Any())
                    {
                        response = $"I found {uniqueFiles.Count} relevant file(s) for you. " +
                                  $"Click on them below to open their location in File Explorer.";
                    }
                    else
                    {
                        response = "I couldn't generate a response. Please try again.";
                    }
                }
                
                var botMessage = new ChatMessage
                {
                    Content = response,
                    IsUser = false,
                    Timestamp = DateTime.Now,
                    RelatedFiles = uniqueFiles.Any() ? uniqueFiles : null
                };

                // Ensure UI thread for collection updates
                Action updateUI = () =>
                {
                    try
                    {
                        if (SelectedChatTab != null)
                        {
                            if (SelectedChatTab.Messages == null)
                            {
                                SelectedChatTab.Messages = new List<ChatMessage>();
                            }
                            SelectedChatTab.Messages.Add(botMessage);
                            SelectedChatTab.LastActivity = DateTime.Now;
                            
                            // Update tab title from first user message if it's still "New Chat"
                            if (SelectedChatTab.Title == "New Chat" && SelectedChatTab.Messages != null && SelectedChatTab.Messages.Any(m => m.IsUser))
                            {
                                try
                                {
                                    var firstUserMessage = SelectedChatTab.Messages.First(m => m.IsUser);
                                    if (firstUserMessage != null && !string.IsNullOrWhiteSpace(firstUserMessage.Content))
                                    {
                                        SelectedChatTab.Title = firstUserMessage.Content.Length > 30 
                                            ? firstUserMessage.Content.Substring(0, 30) + "..." 
                                            : firstUserMessage.Content;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error updating tab title: {ex.Message}");
                                }
                            }
                        }

                        ChatMessages.Add(botMessage);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error adding bot message: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                };

                if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(updateUI);
                }
                else
                {
                    updateUI();
                }

                try
                {
                    SaveCurrentTab();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving tab: {ex.Message}");
                }
            }
            catch (Exception innerEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error in query processing: {innerEx.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {innerEx.StackTrace}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Critical error in SendQuery: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            
            try
            {
                var errorMessage = new ChatMessage
                {
                    Content = $"Error: {ex.Message}. Please try again or check your settings.",
                    IsUser = false,
                    Timestamp = DateTime.Now
                };
                
                // Ensure UI thread for collection updates
                Action updateUI = () =>
                {
                    try
                    {
                        if (SelectedChatTab != null)
                        {
                            if (SelectedChatTab.Messages == null)
                            {
                                SelectedChatTab.Messages = new List<ChatMessage>();
                            }
                            SelectedChatTab.Messages.Add(errorMessage);
                        }
                        ChatMessages.Add(errorMessage);
                    }
                    catch (Exception addEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error adding error message: {addEx.Message}");
                        System.Diagnostics.Debug.WriteLine($"Stack trace: {addEx.StackTrace}");
                    }
                };

                if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(updateUI);
                }
                else
                {
                    updateUI();
                }

                try
                {
                    SaveCurrentTab();
                }
                catch (Exception saveEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving tab after error: {saveEx.Message}");
                }
            }
            catch (Exception innerEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error in error handler: {innerEx.Message}");
            }
        }
        finally
        {
            try
            {
                IsProcessingQuery = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting IsProcessingQuery: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void CreateNewTab()
    {
        var newTab = new ChatTab
        {
            Title = "New Chat",
            CreatedAt = DateTime.Now,
            LastActivity = DateTime.Now
        };
        ChatTabs.Add(newTab);
        SelectedChatTab = newTab;
        UpdateChatMessages();
    }

    [RelayCommand]
    private void SelectTab(ChatTab? tab)
    {
        if (tab != null)
        {
            SaveCurrentTab();
            SelectedChatTab = tab;
            UpdateChatMessages();
        }
    }

    [RelayCommand]
    private void DeleteTab(ChatTab? tab)
    {
        if (tab == null) return;

        SaveCurrentTab();
        _chatHistoryService.DeleteChatTab(tab.Id);
        
        if (ChatTabs.Contains(tab))
        {
            ChatTabs.Remove(tab);
        }

        if (SelectedChatTab == tab)
        {
            SelectedChatTab = ChatTabs.FirstOrDefault();
            if (SelectedChatTab == null)
            {
                CreateNewTab();
            }
            UpdateChatMessages();
        }
    }

    [RelayCommand]
    private void ClearAllChats()
    {
        SaveCurrentTab();
        _chatHistoryService.ClearAllChats();
        ChatTabs.Clear();
        CreateNewTab();
    }

    private void LoadChatTabs()
    {
        try
        {
            var tabs = _chatHistoryService.GetChatTabs();
            ChatTabs.Clear();
            foreach (var tab in tabs)
            {
                ChatTabs.Add(tab);
            }

            if (ChatTabs.Count == 0)
            {
                CreateNewTab();
            }
            else
            {
                // Select the most recently active tab
                SelectedChatTab = ChatTabs.OrderByDescending(t => t.LastActivity).First();
                UpdateChatMessages();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading chat tabs: {ex.Message}");
            CreateNewTab();
        }
    }

    private void SaveCurrentTab()
    {
        if (SelectedChatTab != null)
        {
            _chatHistoryService.SaveChatTab(SelectedChatTab);
        }
    }

    partial void OnSelectedChatTabChanged(ChatTab? value)
    {
        SaveCurrentTab();
        UpdateChatMessages();
    }

    private void UpdateChatMessages()
    {
        // Ensure UI thread for collection updates
        Action updateUI = () =>
        {
            try
            {
                ChatMessages.Clear();
                if (SelectedChatTab?.Messages != null)
                {
                    foreach (var message in SelectedChatTab.Messages)
                    {
                        ChatMessages.Add(message);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating chat messages: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        };

        if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.Invoke(updateUI);
        }
        else
        {
            updateUI();
        }
    }

    [RelayCommand]
    private void OpenFileLocation(SearchResult? result)
    {
        if (result == null) return;

        try
        {
            var filePath = result.File.FilePath;
            if (File.Exists(filePath))
            {
                // Open file location in Windows Explorer and select the file
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else
            {
                MessageBox.Show($"File not found: {filePath}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening file location: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenFileLocationFromList(FileMetadata? file)
    {
        if (file == null) return;

        try
        {
            var filePath = file.FilePath;
            if (File.Exists(filePath))
            {
                // Open file location in Windows Explorer and select the file
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else
            {
                MessageBox.Show($"File not found: {filePath}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening file location: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

