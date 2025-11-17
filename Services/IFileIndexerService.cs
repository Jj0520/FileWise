namespace FileWise.Services;

public interface IFileIndexerService
{
    Task IndexFolderAsync(string folderPath, Action<double>? progressCallback = null, Action<string>? statusCallback = null);
    Task IndexFileAsync(string filePath, Action<string>? statusCallback = null, bool forceReindex = false);
}

