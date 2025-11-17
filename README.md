# FileWise - AI-Powered File Search Application

A Windows desktop application built with C# WPF that enables semantic file search using local AI models via Ollama. FileWise indexes your local files and allows you to query them using natural language.

## Features

- **File Indexing**: Recursively scans folders and extracts text from:
  - `.txt` files
  - `.pdf` files (using PdfPig for text-based PDFs, Tesseract OCR for scanned PDFs, Gemini API as fallback)
  - `.docx` files (using DocumentFormat.OpenXml)
  - `.xlsx` files (using DocumentFormat.OpenXml)
  - `.csv` files

- **Vector Search**: Uses local embedding models (via Ollama) to create semantic embeddings and performs cosine similarity search for fast file retrieval

- **AI Chatbot**: Powered by local LLM models (via Ollama) for natural language understanding and intelligent responses

- **Local Database**: SQLite database stores file metadata and embeddings locally

- **Async Operations**: All file processing and API calls are asynchronous to keep the UI responsive

- **Progress Tracking**: Real-time progress updates during file indexing

## Prerequisites

- .NET 8 SDK
- Windows OS
- **Ollama** installed and running (download from https://ollama.ai)
  - Ollama must be running on `http://localhost:11434`
  - Recommended models:
    - For embeddings: `nomic-embed-text` (run: `ollama pull nomic-embed-text`)
    - For text generation: `llama2`, `mistral`, or `llama3` (run: `ollama pull llama2`)
- **Optional**: `pdfium.dll` for OCR processing of scanned PDFs
  - **Quick Start**: See [QUICK_START_PDFIUM.md](QUICK_START_PDFIUM.md) to download pre-built version
  - **Build from Source**: See [PDFIUM_BUILD.md](PDFIUM_BUILD.md) for detailed instructions
  - If not available, the application will use Gemini API as fallback (requires API key)

## Setup

1. **Install Ollama**:
   - Download and install Ollama from https://ollama.ai
   - Start Ollama service (it should run automatically after installation)

2. **Pull Required Models**:
   ```bash
   ollama pull nomic-embed-text
   ollama pull llama2
   ```
   (You can use other models - just update `appsettings.json` accordingly)

3. **Clone or download this repository**

4. **Configure Models** (optional):
   - Open `appsettings.json`
   - Adjust `TextModel` and `EmbeddingModel` if you're using different models
   - Default models are `llama2` and `nomic-embed-text`

5. **Restore NuGet packages**:
   ```bash
   dotnet restore
   ```

6. **Build the application**:
   ```bash
   dotnet build
   ```

7. **Run the application**:
   ```bash
   dotnet run
   ```

## Usage

1. **Select a Folder**: Click "Select Folder" to choose the directory you want to index

2. **Index Files**: Click "Index Files" to start the indexing process. The progress bar will show the indexing status

3. **Query Files**: Type your question in the chat input at the bottom (e.g., "Find files about budget 2024") and press Enter or click "Send"

4. **View Results**: Search results appear in the bottom panel showing matching files with similarity scores

## Project Structure

```
FileWise/
├── Models/              # Data models (FileMetadata, EmbeddingVector, etc.)
├── Services/            # Business logic services
│   ├── DatabaseService.cs
│   ├── FileIndexerService.cs
│   ├── EmbeddingService.cs
│   ├── VectorSearchService.cs
│   ├── ChatbotService.cs
│   └── TextExtractorService.cs
├── ViewModels/          # MVVM ViewModels
├── Views/               # WPF XAML views
├── Utilities/           # UI converters and utilities
├── App.xaml.cs          # Application startup and DI configuration
└── appsettings.json     # Configuration file
```

## Configuration

Edit `appsettings.json` to customize:

- **LocalModel**: Ollama base URL and model names
  - `BaseUrl`: Default is `http://localhost:11434`
  - `TextModel`: Text generation model (default: `llama2`)
  - `EmbeddingModel`: Embedding model (default: `nomic-embed-text`)
- **Database**: SQLite connection string
- **Indexing**: Chunk size and concurrent file processing limits

## Technologies Used

- **.NET 8**: Framework
- **WPF**: UI framework
- **CommunityToolkit.Mvvm**: MVVM helpers
- **SQLite**: Local database
- **PdfPig**: PDF text extraction (text-based PDFs)
- **Tesseract OCR**: OCR for scanned/image-based PDFs (requires `pdfium.dll`)
- **PdfiumViewer**: PDF rendering for OCR processing
- **Gemini API**: Fallback PDF text extraction when OCR is unavailable
- **DocumentFormat.OpenXml**: Office document processing
- **Ollama**: Local AI model server for embeddings and text generation

## Notes

- The application creates a local SQLite database (`filewise.db`) in the application directory
- Files are only re-indexed if their content hash changes
- Embeddings are generated for text chunks (default: 1000 characters)
- All AI processing happens locally via Ollama - no internet required after models are downloaded
- First-time embedding generation may be slower as models load into memory

## Troubleshooting

- **Connection Error**: Make sure Ollama is running (`ollama serve` or check if it's running as a service)
- **Model Not Found**: Ensure you've pulled the required models (`ollama pull <model-name>`)
- **File Not Indexed**: Check the console output for errors. Some files may fail to extract text
- **Slow Processing**: Local models can be slower than cloud APIs. Consider using smaller/faster models or reducing `MaxConcurrentFiles` in `appsettings.json`
- **Port Issues**: If Ollama is running on a different port, update `BaseUrl` in `appsettings.json`
- **PDF OCR Not Working**: 
  - If you see "Unable to load DLL 'pdfium.dll'", see [PDFIUM_BUILD.md](PDFIUM_BUILD.md) for build instructions
  - The application will automatically fall back to Gemini API if `pdfium.dll` is not available
  - Ensure Gemini API key is configured in `appsettings.json` for fallback to work
- **Scanned PDFs Not Extracting Text**: 
  - Install `pdfium.dll` for local OCR (see [PDFIUM_BUILD.md](PDFIUM_BUILD.md))
  - Or ensure Gemini API key is configured for cloud-based extraction

## License

This project is provided as-is for educational and personal use.

