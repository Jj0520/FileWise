using CommunityToolkit.Mvvm.ComponentModel;

namespace FileWise.Models;

public class FileMetadata : ObservableObject
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime ModifiedDate { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public DateTime IndexedDate { get; set; }
    public string Hash { get; set; } = string.Empty;
    
    private bool _isSelectedForContext = false;
    public bool IsSelectedForContext
    {
        get => _isSelectedForContext;
        set => SetProperty(ref _isSelectedForContext, value);
    }

    private bool _isSelectedForReindex = false;
    public bool IsSelectedForReindex
    {
        get => _isSelectedForReindex;
        set => SetProperty(ref _isSelectedForReindex, value);
    }
}

