# FileWise - AI-Powered File Search Application

A Windows desktop application built with C# WPF that enables semantic file search using either Google Gemini (API key) or local AI models via Ollama. FileWise indexes your local files and allows you to query them using natural language.

## Features

- **File Indexing**: Recursively scans folders and extracts text from:
  - `.txt` files
  - `.pdf` files (using PdfPig for text-based PDFs, Tesseract OCR for scanned PDFs, Gemini API as fallback)
  - `.docx` files (using DocumentFormat.OpenXml)
  - `.xlsx` files (using DocumentFormat.OpenXml)
  - `.csv` files

- **Vector Search**: Uses embedding models (local via Ollama or Gemini cloud) to create semantic embeddings and performs cosine similarity search for fast file retrieval

- **AI Chatbot**: Choose between Gemini (API key) or local Ollama models for natural language understanding and intelligent responses

- **Local Database**: SQLite database stores file metadata and embeddings locally

- **Async Operations**: All file processing and API calls are asynchronous to keep the UI responsive

- **Progress Tracking**: Real-time progress updates during file indexing

## Prerequisites

- .NET 8 SDK
- Windows OS
- **Choose one of the following AI backends:**
  - **Option 1: Ollama (Local)** - Recommended for privacy and offline use
    - Install Ollama from https://ollama.ai
    - Ollama must be running on `http://localhost:11434`
    - Recommended models:
      - For embeddings: `nomic-embed-text` (run: `ollama pull nomic-embed-text`)
      - For text generation: `llama2`, `mistral`, or `llama3` (run: `ollama pull llama2`)
  - **Option 2: Gemini API (Cloud)** - Requires internet connection
    - Get your API key from https://aistudio.google.com/app/apikey
    - **Important**: You must enter your own API key in the Settings window - no API key is included with the application
    - Required for PDF text extraction fallback when OCR is unavailable
- **Optional**: `pdfium.dll` for OCR processing of scanned PDFs
  - **Quick Start**: See [QUICK_START_PDFIUM.md](QUICK_START_PDFIUM.md) to download pre-built version
  - **Build from Source**: See [PDFIUM_BUILD.md](PDFIUM_BUILD.md) for detailed instructions
  - If not available, the application will use Gemini API as fallback (requires API key)

## Setup

1. **Clone or download this repository**

2. **Choose and configure your AI backend:**

   **Option A: Using Ollama (Local - Recommended)**
   - Install Ollama from https://ollama.ai
   - Start Ollama service (it should run automatically after installation)
   - Pull required models:
     ```bash
     ollama pull nomic-embed-text
     ollama pull llama2
     ```
     (You can use other models - just update `appsettings.json` accordingly)
   - In Settings, select "Use Localhost (Local Model)" mode
   - No API key required

   **Option B: Using Gemini API (Cloud)**
   - Get your API key from https://aistudio.google.com/app/apikey
   - Run the application and open Settings
   - Select "Use API Key (Gemini Cloud)" mode
   - Enter your Gemini API key
   - Click "Save API Settings"
   - **Note**: No API key is included with the application - you must provide your own

3. **Configure Models** (optional, only if using Ollama):
   - Open `appsettings.json`
   - Adjust `TextModel` and `EmbeddingModel` if you're using different models
   - Default models are `llama2` and `nomic-embed-text`

4. **Restore NuGet packages**:
   ```bash
   dotnet restore
   ```

5. **Build the application**:
   ```bash
   dotnet build
   ```

6. **Run the application**:
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

### Using the Settings Window (Recommended)

The easiest way to configure FileWise is through the Settings window in the application:

- **API Configuration**: Choose between Gemini cloud API or local Ollama models
  - **API Key Mode**: Enter your Gemini API key (get it from https://aistudio.google.com/app/apikey)
  - **Localhost Mode**: Use local Ollama models (default)
- **Appearance**: Choose Light, Dark, or System theme
- **Indexing**: Configure chunk size and concurrent file processing limits

### Manual Configuration (Advanced)

You can also edit `appsettings.json` directly:

- **Gemini**: API key configuration
  - `ApiKey`: Your Gemini API key (leave empty if using localhost mode)
  - `UseLocalhost`: Set to `"true"` to use local Ollama models, `"false"` for Gemini cloud
  - `LocalhostUrl`: URL for local model server (default: `http://localhost:11434`)
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
- **Gemini API**: Cloud-based embeddings/text generation and PDF fallback when OCR is unavailable
- **DocumentFormat.OpenXml**: Office document processing
- **Ollama**: Local AI model server for embeddings and text generation

## Notes

- The application creates a local SQLite database (`filewise.db`) in the application directory
- Files are only re-indexed if their content hash changes
- Embeddings are generated for text chunks (default: 1000 characters)
- When using Ollama: All AI processing happens locally - no internet required after models are downloaded
- When using Gemini: Internet connection required for AI processing
- First-time embedding generation may be slower as models load into memory (Ollama only)

## Troubleshooting

- **Connection Error (Ollama)**: Make sure Ollama is running (`ollama serve` or check if it's running as a service)
- **Connection Error (Gemini)**: Check your internet connection and verify your API key is correct
- **Model Not Found**: Ensure you've pulled the required models (`ollama pull <model-name>`)
- **File Not Indexed**: Check the console output for errors. Some files may fail to extract text
- **Slow Processing**: Local models can be slower than cloud APIs. Consider using smaller/faster models or reducing `MaxConcurrentFiles` in `appsettings.json`
- **Port Issues**: If Ollama is running on a different port, update `BaseUrl` in `appsettings.json`
- **PDF OCR Not Working**: 
  - If you see "Unable to load DLL 'pdfium.dll'", see [PDFIUM_BUILD.md](PDFIUM_BUILD.md) for build instructions
  - The application will automatically fall back to Gemini API if `pdfium.dll` is not available
  - Ensure Gemini API key is configured in Settings for fallback to work
- **Scanned PDFs Not Extracting Text**: 
  - Install `pdfium.dll` for local OCR (see [PDFIUM_BUILD.md](PDFIUM_BUILD.md))
  - Or configure your Gemini API key in Settings for cloud-based extraction
- **API Key Issues**:
  - Make sure you've entered your own Gemini API key in Settings (no API key is included with the application)
  - Get your API key from https://aistudio.google.com/app/apikey
  - If using localhost mode, you don't need a Gemini API key

## License

This project is provided as-is for educational and personal use.

