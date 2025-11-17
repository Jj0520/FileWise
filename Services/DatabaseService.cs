using System.Data.SQLite;
using FileWise.Models;
using Microsoft.Extensions.Logging;

namespace FileWise.Services;

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(string connectionString, ILogger<DatabaseService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var createFilesTable = @"
            CREATE TABLE IF NOT EXISTS FileMetadata (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FileName TEXT NOT NULL,
                FilePath TEXT NOT NULL UNIQUE,
                FileType TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                ModifiedDate TEXT NOT NULL,
                ExtractedText TEXT,
                IndexedDate TEXT NOT NULL,
                Hash TEXT NOT NULL
            )";

        var createEmbeddingsTable = @"
            CREATE TABLE IF NOT EXISTS EmbeddingVector (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FileMetadataId INTEGER NOT NULL,
                ChunkText TEXT NOT NULL,
                ChunkIndex INTEGER NOT NULL,
                EmbeddingJson TEXT NOT NULL,
                CreatedDate TEXT NOT NULL,
                FOREIGN KEY (FileMetadataId) REFERENCES FileMetadata(Id)
            )";

        using var cmd1 = new SQLiteCommand(createFilesTable, connection);
        await cmd1.ExecuteNonQueryAsync();

        using var cmd2 = new SQLiteCommand(createEmbeddingsTable, connection);
        await cmd2.ExecuteNonQueryAsync();

        _logger.LogInformation("Database initialized");
    }

    public async Task<int> InsertFileMetadataAsync(FileMetadata metadata)
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            INSERT INTO FileMetadata (FileName, FilePath, FileType, FileSize, ModifiedDate, ExtractedText, IndexedDate, Hash)
            VALUES (@FileName, @FilePath, @FileType, @FileSize, @ModifiedDate, @ExtractedText, @IndexedDate, @Hash);
            SELECT last_insert_rowid();";

        using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@FileName", metadata.FileName);
        cmd.Parameters.AddWithValue("@FilePath", metadata.FilePath);
        cmd.Parameters.AddWithValue("@FileType", metadata.FileType);
        cmd.Parameters.AddWithValue("@FileSize", metadata.FileSize);
        cmd.Parameters.AddWithValue("@ModifiedDate", metadata.ModifiedDate.ToString("O"));
        cmd.Parameters.AddWithValue("@ExtractedText", metadata.ExtractedText);
        cmd.Parameters.AddWithValue("@IndexedDate", metadata.IndexedDate.ToString("O"));
        cmd.Parameters.AddWithValue("@Hash", metadata.Hash);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateFileMetadataAsync(FileMetadata metadata)
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            UPDATE FileMetadata 
            SET FileName = @FileName, FileType = @FileType, FileSize = @FileSize, 
                ModifiedDate = @ModifiedDate, ExtractedText = @ExtractedText, 
                IndexedDate = @IndexedDate, Hash = @Hash
            WHERE FilePath = @FilePath";

        using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@FileName", metadata.FileName);
        cmd.Parameters.AddWithValue("@FilePath", metadata.FilePath);
        cmd.Parameters.AddWithValue("@FileType", metadata.FileType);
        cmd.Parameters.AddWithValue("@FileSize", metadata.FileSize);
        cmd.Parameters.AddWithValue("@ModifiedDate", metadata.ModifiedDate.ToString("O"));
        cmd.Parameters.AddWithValue("@ExtractedText", metadata.ExtractedText);
        cmd.Parameters.AddWithValue("@IndexedDate", metadata.IndexedDate.ToString("O"));
        cmd.Parameters.AddWithValue("@Hash", metadata.Hash);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<FileMetadata?> GetFileMetadataByPathAsync(string filePath)
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT * FROM FileMetadata WHERE FilePath = @FilePath";
        using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@FilePath", filePath);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var fileType = reader.GetString(3);
            // Normalize FileType to lowercase with dot
            if (!string.IsNullOrEmpty(fileType))
            {
                fileType = fileType.ToLowerInvariant().Trim();
                if (!fileType.StartsWith("."))
                {
                    fileType = "." + fileType;
                }
            }

            return new FileMetadata
            {
                Id = reader.GetInt32(0),
                FileName = reader.GetString(1),
                FilePath = reader.GetString(2),
                FileType = fileType,
                FileSize = reader.GetInt64(4),
                ModifiedDate = DateTime.Parse(reader.GetString(5)),
                ExtractedText = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                IndexedDate = DateTime.Parse(reader.GetString(7)),
                Hash = reader.GetString(8)
            };
        }

        return null;
    }

    public async Task<List<FileMetadata>> GetAllFilesAsync()
    {
        var files = new List<FileMetadata>();
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT * FROM FileMetadata";
        using var cmd = new SQLiteCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var fileType = reader.GetString(3);
            // Normalize FileType to lowercase with dot
            if (!string.IsNullOrEmpty(fileType))
            {
                fileType = fileType.ToLowerInvariant().Trim();
                if (!fileType.StartsWith("."))
                {
                    fileType = "." + fileType;
                }
            }

            files.Add(new FileMetadata
            {
                Id = reader.GetInt32(0),
                FileName = reader.GetString(1),
                FilePath = reader.GetString(2),
                FileType = fileType,
                FileSize = reader.GetInt64(4),
                ModifiedDate = DateTime.Parse(reader.GetString(5)),
                ExtractedText = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                IndexedDate = DateTime.Parse(reader.GetString(7)),
                Hash = reader.GetString(8)
            });
        }

        return files;
    }

    public async Task InsertEmbeddingAsync(EmbeddingVector embedding)
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            INSERT INTO EmbeddingVector (FileMetadataId, ChunkText, ChunkIndex, EmbeddingJson, CreatedDate)
            VALUES (@FileMetadataId, @ChunkText, @ChunkIndex, @EmbeddingJson, @CreatedDate)";

        using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@FileMetadataId", embedding.FileMetadataId);
        cmd.Parameters.AddWithValue("@ChunkText", embedding.ChunkText);
        cmd.Parameters.AddWithValue("@ChunkIndex", embedding.ChunkIndex);
        cmd.Parameters.AddWithValue("@EmbeddingJson", embedding.EmbeddingJson);
        cmd.Parameters.AddWithValue("@CreatedDate", embedding.CreatedDate.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<EmbeddingVector>> GetEmbeddingsByFileIdAsync(int fileMetadataId)
    {
        var embeddings = new List<EmbeddingVector>();
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT * FROM EmbeddingVector WHERE FileMetadataId = @FileMetadataId ORDER BY ChunkIndex";
        using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@FileMetadataId", fileMetadataId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            embeddings.Add(new EmbeddingVector
            {
                Id = reader.GetInt32(0),
                FileMetadataId = reader.GetInt32(1),
                ChunkText = reader.GetString(2),
                ChunkIndex = reader.GetInt32(3),
                EmbeddingJson = reader.GetString(4),
                CreatedDate = DateTime.Parse(reader.GetString(5))
            });
        }

        return embeddings;
    }

    public async Task<List<EmbeddingVector>> GetAllEmbeddingsAsync()
    {
        var embeddings = new List<EmbeddingVector>();
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT * FROM EmbeddingVector";
        using var cmd = new SQLiteCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            embeddings.Add(new EmbeddingVector
            {
                Id = reader.GetInt32(0),
                FileMetadataId = reader.GetInt32(1),
                ChunkText = reader.GetString(2),
                ChunkIndex = reader.GetInt32(3),
                EmbeddingJson = reader.GetString(4),
                CreatedDate = DateTime.Parse(reader.GetString(5))
            });
        }

        return embeddings;
    }

    public async Task<bool> FileExistsAsync(string filePath, string hash)
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT COUNT(*) FROM FileMetadata WHERE FilePath = @FilePath AND Hash = @Hash";
        using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@FilePath", filePath);
        cmd.Parameters.AddWithValue("@Hash", hash);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    public async Task DeleteEmbeddingsByFileIdAsync(int fileMetadataId)
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM EmbeddingVector WHERE FileMetadataId = @FileMetadataId";
        using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@FileMetadataId", fileMetadataId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteFileMetadataAsync(int fileMetadataId)
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        // Delete embeddings first (foreign key constraint)
        await DeleteEmbeddingsByFileIdAsync(fileMetadataId);

        // Then delete the file metadata
        var sql = "DELETE FROM FileMetadata WHERE Id = @FileMetadataId";
        using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@FileMetadataId", fileMetadataId);

        await cmd.ExecuteNonQueryAsync();
    }
}

