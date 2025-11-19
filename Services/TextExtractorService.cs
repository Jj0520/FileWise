using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Tesseract;
using Ghostscript.NET;
using Ghostscript.NET.Rasterizer;
using System.Drawing;
using System.Drawing.Imaging;
using FileWise.Utilities;

namespace FileWise.Services;

public class TextExtractorService
{
    private readonly IConfiguration? _configuration;
    private readonly UserSettingsService? _userSettingsService;
    private readonly HttpClient _httpClient;
    private const string GeminiApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
    private const string GeminiFileApiUrl = "https://generativelanguage.googleapis.com/upload/v1beta/files";
    private const long FileSizeThreshold = 20 * 1024 * 1024; // 20MB threshold
    
    public TextExtractorService(IConfiguration? configuration = null, UserSettingsService? userSettingsService = null)
    {
        _configuration = configuration;
        _userSettingsService = userSettingsService;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10); // Longer timeout for PDF processing via Gemini API
    }
    public async Task<string> ExtractTextAsync(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            return extension switch
            {
            ".txt" => await Task.Run(() => File.ReadAllText(filePath)),
            ".csv" => await Task.Run(() => ExtractCsvText(filePath)),
            ".pdf" => await ExtractPdfTextAsync(filePath),
            ".docx" => await Task.Run(() => ExtractDocxText(filePath)),
            ".xlsx" => await Task.Run(() => ExtractXlsxText(filePath)),
                _ => string.Empty
            };
    }

    private string ExtractCsvText(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        return string.Join(" ", lines);
    }

    private async Task<string> ExtractPdfTextAsync(string filePath)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"Starting PDF text extraction for: {Path.GetFileName(filePath)}");
            Console.WriteLine($"📄 Processing PDF: {Path.GetFileName(filePath)}");
            
            // Use Tesseract OCR (renders PDF pages to images, then OCRs them - like OCRmyPDF)
            try
            {
                var tesseractText = await ExtractPdfTextWithTesseractAsync(filePath).ConfigureAwait(false);
                var tesseractLength = tesseractText?.Length ?? 0;
                
                System.Diagnostics.Debug.WriteLine($"Tesseract OCR extraction completed: {tesseractLength} characters");
                Console.WriteLine($"  📊 Tesseract OCR returned {tesseractLength} characters");
                
                if (!string.IsNullOrWhiteSpace(tesseractText) && tesseractText.Length > 10)
                {
                    System.Diagnostics.Debug.WriteLine($"✓ Tesseract OCR successfully extracted {tesseractText.Length} characters from {Path.GetFileName(filePath)}");
                    Console.WriteLine($"✓ Tesseract OCR extracted {tesseractText.Length} characters");
                    return tesseractText;
                }
                else
                {
                    // Tesseract OCR failed or returned little text, try Gemini as fallback
                    Console.WriteLine($"  ⚠️ Tesseract OCR returned {tesseractLength} chars, trying Gemini as fallback...");
                    string? geminiText = null;
                    int geminiLength = 0;
                    string? geminiError = null;
                    
                    try
                    {
                        geminiText = await ExtractPdfTextWithGeminiAsync(filePath).ConfigureAwait(false);
                        geminiLength = geminiText?.Length ?? 0;
                        
                        if (!string.IsNullOrWhiteSpace(geminiText) && geminiText.Length > 10)
                        {
                            System.Diagnostics.Debug.WriteLine($"✓ Gemini successfully extracted {geminiText.Length} characters from {Path.GetFileName(filePath)}");
                            Console.WriteLine($"✓ Gemini extracted {geminiText.Length} characters");
                            return geminiText;
                        }
                    }
                    catch (Exception geminiEx)
                    {
                        geminiError = geminiEx.Message;
                        System.Diagnostics.Debug.WriteLine($"Gemini fallback failed: {geminiEx.Message}");
                        Console.WriteLine($"  ⚠️ Gemini fallback failed: {geminiEx.Message}");
                    }
                    
                    // Return the best result we have (even if small, it's better than nothing)
                    if (geminiLength > tesseractLength)
                    {
                        Console.WriteLine($"  ⚠️ Returning Gemini result ({geminiLength} chars) as best available");
                        return geminiText ?? string.Empty;
                    }
                    else if (tesseractLength > 0)
                    {
                        Console.WriteLine($"  ⚠️ Returning Tesseract OCR result ({tesseractLength} chars) as best available");
                        return tesseractText ?? string.Empty;
                    }
                    
                    // If all failed, log detailed error and return empty
                    var errorMsg = $"All extraction methods returned no text. Tesseract: {tesseractLength} chars, Gemini: {geminiLength} chars";
                    if (!string.IsNullOrEmpty(geminiError))
                    {
                        errorMsg += $", Gemini error: {geminiError}";
                    }
                    System.Diagnostics.Debug.WriteLine(errorMsg);
                    Console.WriteLine($"✗ {errorMsg}");
                    return string.Empty;
                }
            }
            catch (Exception ocrEx)
            {
                var errorMsg = $"Tesseract OCR failed for {filePath}: {ocrEx.Message}";
                System.Diagnostics.Debug.WriteLine(errorMsg);
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ocrEx.StackTrace}");
                Console.WriteLine($"✗ {errorMsg}");
                
                // Try Gemini as fallback
                try
                {
                    Console.WriteLine($"  🔄 Trying Gemini as fallback...");
                    var geminiText = await ExtractPdfTextWithGeminiAsync(filePath).ConfigureAwait(false);
                    var geminiLength = geminiText?.Length ?? 0;
                    
                    if (!string.IsNullOrWhiteSpace(geminiText) && geminiText.Length > 10)
                    {
                        Console.WriteLine($"✓ Gemini extracted {geminiText.Length} characters");
                        return geminiText;
                    }
                    else if (geminiLength > 0)
                    {
                        Console.WriteLine($"  ⚠️ Gemini returned {geminiLength} chars (below threshold), using it anyway");
                        return geminiText;
                    }
                }
                catch (Exception geminiEx)
                {
                    Console.WriteLine($"  ✗ Gemini fallback also failed: {geminiEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"Gemini fallback exception: {geminiEx.Message}\nStack: {geminiEx.StackTrace}");
                }
                
                return string.Empty;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error extracting PDF text from {filePath}: {ex.Message}");
            Console.WriteLine($"✗ Error extracting PDF: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Extract text from PDF using Tesseract OCR (for scanned/image-based PDFs)
    /// Uses Ghostscript.NET to render PDF pages to images, then Tesseract OCR to extract text
    /// Based on OCRmyPDF approach: https://github.com/ocrmypdf/OCRmyPDF
    /// and Tesseract OCR engine: https://github.com/tesseract-ocr/tesseract
    /// </summary>
    private async Task<string> ExtractPdfTextWithTesseractAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var textBuilder = new StringBuilder();
                
                // Get tessdata path - Tesseract looks for tessdata folder in the executable directory
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var exeDirectory = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(exeDirectory))
                {
                    exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
                }
                var tessdataPath = Path.Combine(exeDirectory, "tessdata");
                
                // If tessdata doesn't exist in exe directory, try current directory
                if (!Directory.Exists(tessdataPath))
                {
                    tessdataPath = Path.Combine(Directory.GetCurrentDirectory(), "tessdata");
                }
                
                // If still doesn't exist, use default (usually in Program Files or system)
                if (!Directory.Exists(tessdataPath))
                {
                    tessdataPath = @"./tessdata"; // Tesseract will search in current directory
                }
                
                Console.WriteLine($"  📂 Using Tesseract data path: {tessdataPath}");
                System.Diagnostics.Debug.WriteLine($"Tesseract data path: {tessdataPath}");
                
                // Verify tessdata exists
                var engDataPath = Path.Combine(tessdataPath, "eng.traineddata");
                if (!File.Exists(engDataPath))
                {
                    var errorMsg = $"Tesseract language data not found at: {engDataPath}";
                    Console.WriteLine($"  ✗ {errorMsg}");
                    System.Diagnostics.Debug.WriteLine(errorMsg);
                    return string.Empty;
                }
                Console.WriteLine($"  ✓ Found eng.traineddata ({new FileInfo(engDataPath).Length / 1024 / 1024} MB)");
                
                // Determine OCR language based on settings
                string ocrLanguage = "eng";
                bool enableTraditionalChinese = _userSettingsService?.EnableTraditionalChinese ?? false;
                
                if (enableTraditionalChinese)
                {
                    var chiTraDataPath = Path.Combine(tessdataPath, "chi_tra.traineddata");
                    if (File.Exists(chiTraDataPath))
                    {
                        ocrLanguage = "eng+chi_tra";
                        Console.WriteLine($"  ✓ Found chi_tra.traineddata ({new FileInfo(chiTraDataPath).Length / 1024 / 1024} MB)");
                        Console.WriteLine($"  🌏 Using OCR languages: English + Traditional Chinese");
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠️ Traditional Chinese enabled but chi_tra.traineddata not found at: {chiTraDataPath}");
                        Console.WriteLine($"  💡 Please download chi_tra.traineddata from https://github.com/tesseract-ocr/tessdata");
                        Console.WriteLine($"  📝 Using English only for OCR");
                    }
                }
                
                // Initialize Tesseract engine once for all pages
                Console.WriteLine($"  🔧 Initializing Tesseract engine with language: {ocrLanguage}...");
                TesseractEngine? engine = null;
                try
                {
                    engine = new TesseractEngine(tessdataPath, ocrLanguage, EngineMode.Default);
                    Console.WriteLine($"  ✓ Tesseract engine initialized with language: {ocrLanguage}");
                }
                catch (Exception engineEx)
                {
                    var errorMsg = $"Failed to initialize Tesseract engine: {engineEx.Message}";
                    Console.WriteLine($"  ✗ {errorMsg}");
                    System.Diagnostics.Debug.WriteLine(errorMsg);
                    System.Diagnostics.Debug.WriteLine($"Stack trace: {engineEx.StackTrace}");
                    return string.Empty;
                }
                
                // Declare tempFilePath outside try block so it's accessible in finally
                string? tempFilePath = null;
                string actualFilePath = filePath;
                
                try
                {
                    // Validate file exists and is readable
                    if (!File.Exists(filePath))
                    {
                        var errorMsg = $"PDF file not found: {filePath}";
                        Console.WriteLine($"  ✗ {errorMsg}");
                        System.Diagnostics.Debug.WriteLine(errorMsg);
                        return string.Empty;
                    }
                    
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length == 0)
                    {
                        var errorMsg = $"PDF file is empty: {filePath}";
                        Console.WriteLine($"  ✗ {errorMsg}");
                        System.Diagnostics.Debug.WriteLine(errorMsg);
                        return string.Empty;
                    }
                    
                    // Verify it's actually a PDF by checking file header
                    // Some PDFs may have leading whitespace, BOM, or other data before the %PDF signature
                    bool isValidPdf = false;
                    int pdfSignatureOffset = -1;
                    try
                    {
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                        {
                            // Read first 10KB to search for PDF signature (some files have large headers)
                            var searchSize = Math.Min(10 * 1024, (int)fileInfo.Length);
                            var buffer = new byte[searchSize];
                            var bytesRead = fs.Read(buffer, 0, buffer.Length);
                            
                            // Check first 4 bytes
                            var header = new byte[4];
                            Array.Copy(buffer, 0, header, 0, Math.Min(4, bytesRead));
                            var headerStr = System.Text.Encoding.ASCII.GetString(header);
                            var headerHex = BitConverter.ToString(header).Replace("-", " ");
                            
                            Console.WriteLine($"  🔍 PDF header check: '{headerStr}' (hex: {headerHex})");
                            System.Diagnostics.Debug.WriteLine($"PDF header: '{headerStr}' (hex: {headerHex})");
                            
                            if (headerStr == "%PDF")
                            {
                                isValidPdf = true;
                                pdfSignatureOffset = 0;
                                Console.WriteLine($"  ✓ PDF header validated at offset 0 (file size: {fileInfo.Length / 1024 / 1024}MB)");
                            }
                            else
                            {
                                // Search for %PDF signature in the buffer (byte-by-byte for accuracy)
                                for (int i = 0; i <= bytesRead - 4; i++)
                                {
                                    if (buffer[i] == 0x25 && buffer[i + 1] == 0x50 && buffer[i + 2] == 0x44 && buffer[i + 3] == 0x46) // "%PDF"
                                    {
                                        isValidPdf = true;
                                        pdfSignatureOffset = i;
                                        Console.WriteLine($"  ✓ PDF signature found at offset {i} (file may have leading data)");
                                        System.Diagnostics.Debug.WriteLine($"PDF signature found at offset {i}");
                                        break;
                                    }
                                }
                                
                                if (!isValidPdf)
                                {
                                    // Show more diagnostic info
                                    var preview = headerStr.Replace('\0', '?').Replace('\r', '?').Replace('\n', '?');
                                    Console.WriteLine($"  ⚠️ PDF signature '%PDF' not found in first {bytesRead} bytes");
                                    Console.WriteLine($"  📋 First 4 bytes: '{preview}' (hex: {headerHex})");
                                    System.Diagnostics.Debug.WriteLine($"PDF signature not found. First 4 bytes: '{preview}' (hex: {headerHex})");
                                    
                                    // Check if it might be a different file type
                                    var bufferStr = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(100, bytesRead));
                                    if (headerStr.StartsWith("PK") || bufferStr.Contains("PK"))
                                    {
                                        Console.WriteLine($"  ⚠️ File appears to be a ZIP archive (might be a corrupted PDF or different format)");
                                        System.Diagnostics.Debug.WriteLine("File appears to be ZIP format");
                                    }
                                    else if (header.Length >= 2 && header[0] == 0xFF && header[1] == 0xD8)
                                    {
                                        Console.WriteLine($"  ⚠️ File appears to be a JPEG image");
                                        System.Diagnostics.Debug.WriteLine("File appears to be JPEG format");
                                    }
                                    else if (header.Length >= 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                                    {
                                        Console.WriteLine($"  ⚠️ File appears to be a PNG image");
                                        System.Diagnostics.Debug.WriteLine("File appears to be PNG format");
                                    }
                                    else
                                    {
                                        // Show first 16 bytes in hex for better diagnostics
                                        var hexPreview = BitConverter.ToString(buffer, 0, Math.Min(16, bytesRead)).Replace("-", " ");
                                        Console.WriteLine($"  📋 First 16 bytes (hex): {hexPreview}");
                                        System.Diagnostics.Debug.WriteLine($"First 16 bytes (hex): {hexPreview}");
                                        
                                        // Check for WPS Office indicators or other document formats
                                        var bufferStrLarge = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(1000, bytesRead));
                                        var bufferStrLower = bufferStrLarge.ToLower();
                                        
                                        if (bufferStrLower.Contains("wps") || bufferStrLower.Contains("kingsoft"))
                                        {
                                            Console.WriteLine($"  📄 File may be a WPS Office document");
                                            Console.WriteLine($"  💡 WPS Office PDFs should be standard PDFs, but this file appears corrupted or encrypted");
                                            Console.WriteLine($"  💡 Try opening in WPS Office and re-exporting as a standard PDF");
                                            System.Diagnostics.Debug.WriteLine("File may be WPS Office format");
                                        }
                                        else if (bufferStrLower.Contains("encrypt") || bufferStrLower.Contains("password") || 
                                                 bufferStrLower.Contains("security") || headerHex.Contains("17 DA"))
                                        {
                                            Console.WriteLine($"  🔒 File may be encrypted or password-protected");
                                            Console.WriteLine($"  💡 Encrypted PDFs may not have standard headers");
                                            Console.WriteLine($"  💡 Try opening in a PDF viewer to verify and remove encryption if needed");
                                            System.Diagnostics.Debug.WriteLine("File may be encrypted");
                                        }
                                        else
                                        {
                                            Console.WriteLine($"  ⚠️ File does not appear to be a valid PDF format");
                                        }
                                    }
                                    
                                    // Don't return early - let Ghostscript try to open it anyway
                                    // Some PDFs might be valid but have unusual headers (e.g., encrypted, WPS Office variants)
                                    Console.WriteLine($"  ⚠️ Continuing anyway - Ghostscript may still be able to process it");
                                }
                            }
                        }
                    }
                    catch (Exception headerEx)
                    {
                        var errorMsg = $"Failed to validate PDF header: {headerEx.Message}";
                        Console.WriteLine($"  ⚠️ {errorMsg}");
                        System.Diagnostics.Debug.WriteLine(errorMsg);
                        System.Diagnostics.Debug.WriteLine($"Stack trace: {headerEx.StackTrace}");
                        // Continue anyway - might still be a valid PDF that Ghostscript can handle
                    }
                    
                    // Render PDF pages to images using Ghostscript.NET (like OCRmyPDF does)
                    // 
                    // NOTE: Recent Ghostscript versions use the new GhostPDF interpreter (written in C) as the default.
                    // This new interpreter, along with security enhancements, may cause PageCount to return 0 even
                    // when pages are accessible. The workaround below attempts to access pages directly to discover
                    // the actual page count.
                    //
                    // Ghostscript may also have issues with very long file paths on Windows (MAX_PATH = 260 chars)
                    // Copy to temp file with shorter path if needed
                    actualFilePath = filePath; // Use the variable declared outside try block
                    const int MaxPathLength = 250; // Leave some margin below 260
                    
                    if (filePath.Length > MaxPathLength)
                    {
                        try
                        {
                            Console.WriteLine($"  📁 File path is {filePath.Length} characters (may exceed Windows MAX_PATH limit)");
                            Console.WriteLine($"  📋 Copying to temporary file with shorter path...");
                            
                            var tempDir = Path.GetTempPath();
                            var tempFileName = $"fw_{Guid.NewGuid():N}.pdf";
                            tempFilePath = Path.Combine(tempDir, tempFileName);
                            
                            File.Copy(filePath, tempFilePath, overwrite: true);
                            actualFilePath = tempFilePath;
                            
                            Console.WriteLine($"  ✓ Copied to temp file: {Path.GetFileName(tempFilePath)}");
                            System.Diagnostics.Debug.WriteLine($"Copied PDF to temp file: {tempFilePath}");
                        }
                        catch (Exception copyEx)
                        {
                            Console.WriteLine($"  ⚠️ Failed to copy to temp file: {copyEx.Message}. Using original path.");
                            System.Diagnostics.Debug.WriteLine($"Temp file copy failed: {copyEx.Message}");
                            // Continue with original path
                        }
                    }
                    
                    try
                    {
                    Console.WriteLine($"  📖 Rendering PDF pages to images with Ghostscript...");
                    using (var rasterizer = new GhostscriptRasterizer())
                    {
                        try
                        {
                                Console.WriteLine($"  🔓 Opening PDF with Ghostscript: {Path.GetFileName(actualFilePath)}");
                                rasterizer.Open(actualFilePath);
                                Console.WriteLine($"  ✓ PDF opened successfully with Ghostscript");
                        }
                        catch (Exception gsEx) when (gsEx.Message.Contains("Ghostscript native library"))
                        {
                            var errorMsg = "Ghostscript native library is not installed. Please install Ghostscript from https://www.ghostscript.com/download/gsdnld.html (64-bit version required)";
                            Console.WriteLine($"  ✗ {errorMsg}");
                            System.Diagnostics.Debug.WriteLine(errorMsg);
                            System.Diagnostics.Debug.WriteLine($"Ghostscript error: {gsEx.Message}");
                                System.Diagnostics.Debug.WriteLine($"Stack trace: {gsEx.StackTrace}");
                            return string.Empty;
                        }
                        catch (Exception gsEx)
                        {
                            var errorMsg = $"Failed to open PDF with Ghostscript: {gsEx.Message}";
                            Console.WriteLine($"  ✗ {errorMsg}");
                            System.Diagnostics.Debug.WriteLine(errorMsg);
                                System.Diagnostics.Debug.WriteLine($"Exception type: {gsEx.GetType().FullName}");
                            System.Diagnostics.Debug.WriteLine($"Stack trace: {gsEx.StackTrace}");
                                
                                // Try to get inner exception details
                                if (gsEx.InnerException != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Inner exception: {gsEx.InnerException.Message}");
                                    System.Diagnostics.Debug.WriteLine($"Inner exception type: {gsEx.InnerException.GetType().FullName}");
                                }
                            return string.Empty;
                        }
                        
                        // Get page count with detailed diagnostics
                        int pageCount;
                        try
                        {
                            pageCount = rasterizer.PageCount;
                            Console.WriteLine($"  📄 Ghostscript reports {pageCount} page(s)");
                            System.Diagnostics.Debug.WriteLine($"Ghostscript PageCount property: {pageCount}");
                        }
                        catch (Exception pageCountEx)
                        {
                            var errorMsg = $"Failed to get page count from Ghostscript: {pageCountEx.Message}";
                            Console.WriteLine($"  ✗ {errorMsg}");
                            System.Diagnostics.Debug.WriteLine(errorMsg);
                            System.Diagnostics.Debug.WriteLine($"Stack trace: {pageCountEx.StackTrace}");
                            return string.Empty;
                        }
                        
                        Console.WriteLine($"  📄 Processing {pageCount} page(s) with Tesseract OCR...");
                        
                        if (pageCount == 0)
                        {
                            var errorMsg = $"Ghostscript opened the PDF but found 0 pages. The PDF may be corrupted, encrypted, or in an unsupported format.";
                            Console.WriteLine($"  ⚠️ {errorMsg}");
                            System.Diagnostics.Debug.WriteLine(errorMsg);
                            System.Diagnostics.Debug.WriteLine($"File path: {filePath}");
                            System.Diagnostics.Debug.WriteLine($"File size: {fileInfo.Length} bytes");
                            System.Diagnostics.Debug.WriteLine($"File exists: {File.Exists(filePath)}");
                            
                            // Try to work around GhostPDF/security issue where PageCount is 0 but pages are accessible
                            // This is a known issue with newer Ghostscript versions using the new GhostPDF interpreter
                            // and security restrictions that prevent accessing internal page count information
                            Console.WriteLine($"  🔍 Attempting to access pages directly (workaround for GhostPDF PageCount=0 issue)...");
                            int actualPageCount = 0;
                            bool foundPages = false;
                            
                            // Try to access pages sequentially until we get an error
                            for (int testPage = 1; testPage <= 100; testPage++) // Limit to 100 pages for safety
                            {
                                try
                                {
                                    using (var testPageImage = rasterizer.GetPage(300, testPage))
                                    {
                                        if (testPageImage != null)
                                        {
                                            actualPageCount = testPage;
                                            foundPages = true;
                                            if (testPage <= 3 || testPage % 10 == 0)
                                            {
                                                Console.WriteLine($"  ✓ Page {testPage} is accessible (PageCount was wrong!)");
                                            }
                                        }
                                    }
                                }
                                catch (Exception pageEx)
                                {
                                    // Expected when we've reached the end of pages
                                    if (testPage == 1)
                                    {
                                        // First page failed - PDF really has no accessible pages
                                        Console.WriteLine($"  ✗ Cannot access page 1: {pageEx.Message}");
                                        System.Diagnostics.Debug.WriteLine($"Page 1 access failed: {pageEx.Message}");
                                        System.Diagnostics.Debug.WriteLine($"Exception type: {pageEx.GetType().FullName}");
                                        
                                        // Provide clearer error message based on the issue
                                        if (!isValidPdf && pdfSignatureOffset == -1)
                                        {
                                            Console.WriteLine($"  ❌ File does not appear to be a valid PDF file.");
                                            Console.WriteLine($"  💡 The file may be corrupted, encrypted, or in a different format.");
                                            Console.WriteLine($"  💡 Try opening the file in a PDF viewer to verify it's valid.");
                                            System.Diagnostics.Debug.WriteLine("File failed validation: not a valid PDF format");
                                        }
                                        
                                        // Check if it's an encryption/password issue
                                        if (pageEx.Message.Contains("password") || pageEx.Message.Contains("encrypt") || 
                                            pageEx.Message.Contains("Permission") || pageEx.Message.Contains("security"))
                                        {
                                            Console.WriteLine($"  🔒 PDF appears to be password-protected or encrypted");
                                            System.Diagnostics.Debug.WriteLine("PDF may be password-protected or encrypted");
                                        }
                                        else if (pageEx.Message.Contains("range") || pageEx.Message.Contains("invalid"))
                                        {
                                            Console.WriteLine($"  ⚠️ Ghostscript cannot read pages from this file - it may not be a valid PDF");
                                            System.Diagnostics.Debug.WriteLine("Ghostscript reports invalid page range");
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        // We successfully read some pages, now hit the end
                                        Console.WriteLine($"  ✓ Found {actualPageCount} accessible page(s) (PageCount property was incorrect)");
                                        break;
                                    }
                                }
                            }
                            
                            if (foundPages && actualPageCount > 0)
                            {
                                // Use the actual page count we discovered
                                pageCount = actualPageCount;
                                Console.WriteLine($"  🔧 Using discovered page count: {pageCount}");
                            }
                            else
                            {
                                // No pages accessible
                                Console.WriteLine($"  ✗ No accessible pages found in PDF");
                            return string.Empty;
                            }
                        }
                        
                        // Process each page
                        for (int pageNum = 1; pageNum <= pageCount; pageNum++)
                        {
                            try
                            {
                                Console.WriteLine($"  🔍 Processing page {pageNum}/{pageCount}...");
                                
                                // Render page to image at 300 DPI (good quality for OCR)
                                using (var pageImage = rasterizer.GetPage(300, pageNum))
                                {
                                    // Convert System.Drawing.Image to byte array for Tesseract
                                    byte[] imageBytes;
                                    using (var ms = new MemoryStream())
                                    {
                                        pageImage.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                        imageBytes = ms.ToArray();
                                    }
                                    
                                    // Process image with Tesseract
                                    using (var pix = Pix.LoadFromMemory(imageBytes))
                                    {
                                        if (pix != null)
                                        {
                                            using (var page = engine.Process(pix))
                                            {
                                                var pageText = page.GetText();
                                                if (!string.IsNullOrWhiteSpace(pageText))
                                                {
                                                    textBuilder.AppendLine(pageText);
                                                    Console.WriteLine($"  ✓ Page {pageNum}: {pageText.Length} characters");
                                                }
                                                else
                                                {
                                                    Console.WriteLine($"  ⚠️ Page {pageNum}: No text found");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception pageEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error processing page {pageNum}: {pageEx.Message}");
                                Console.WriteLine($"  ⚠️ Error processing page {pageNum}: {pageEx.Message}");
                                // Continue with next page
                            }
                        }
                    }
                    }
                    catch (Exception rasterizerEx)
                    {
                        var errorMsg = $"Error during PDF rasterization: {rasterizerEx.Message}";
                        Console.WriteLine($"  ✗ {errorMsg}");
                        System.Diagnostics.Debug.WriteLine(errorMsg);
                        System.Diagnostics.Debug.WriteLine($"Stack trace: {rasterizerEx.StackTrace}");
                        // Continue to cleanup - don't return here as we need to clean up temp file
                    }
                }
                finally
                {
                    try
                    {
                        engine?.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error disposing Tesseract engine: {disposeEx.Message}");
                        // Ignore disposal errors
                    }
                    
                    // Clean up temporary file if we created one
                    if (!string.IsNullOrEmpty(tempFilePath) && File.Exists(tempFilePath))
                    {
                        try
                        {
                            File.Delete(tempFilePath);
                            Console.WriteLine($"  🗑️ Deleted temporary file");
                            System.Diagnostics.Debug.WriteLine($"Deleted temp file: {tempFilePath}");
                        }
                        catch (Exception deleteEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to delete temp file: {deleteEx.Message}");
                            // Ignore deletion errors - temp file will be cleaned up by system eventually
                        }
                    }
                }
                
                var result = textBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(result))
                {
                    Console.WriteLine($"  ✓ Tesseract OCR extracted {result.Length} characters total");
                }
                else
                {
                    Console.WriteLine($"  ✗ Tesseract OCR found no text");
                }
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tesseract OCR extraction error for {filePath}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                Console.WriteLine($"  ✗ Tesseract OCR error: {ex.Message}");
                return string.Empty;
            }
        });
    }


    private async Task<string> ExtractPdfTextWithGeminiAsync(string filePath)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"Starting Gemini extraction for: {Path.GetFileName(filePath)}");
            Console.WriteLine($"  🔍 Using Gemini 2.5 Flash to extract text from PDF...");
            
            // Get API key from configuration
            var apiKey = _configuration?["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var errorMsg = "Gemini API key not configured.";
                System.Diagnostics.Debug.WriteLine(errorMsg);
                Console.WriteLine($"  ✗ {errorMsg}");
                return string.Empty;
            }
            
            // Check file size to determine which method to use
            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;
            
            if (fileSize >= FileSizeThreshold)
            {
                // Use File API for larger files (>= 20MB)
                System.Diagnostics.Debug.WriteLine($"File size ({fileSize / 1024 / 1024}MB) exceeds 20MB threshold, using File API");
                Console.WriteLine($"  📤 Using File API for large PDF ({fileSize / 1024 / 1024}MB)...");
                return await ExtractPdfWithFileApiAsync(filePath, apiKey);
            }
            else
            {
                // Use inline base64 for smaller files (< 20MB)
                System.Diagnostics.Debug.WriteLine($"File size ({fileSize / 1024 / 1024}MB) under 20MB, using inline base64");
                Console.WriteLine($"  📤 Using inline base64 for PDF ({fileSize / 1024 / 1024}MB)...");
                return await ExtractPdfWithInlineDataAsync(filePath, apiKey);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gemini extraction failed for {filePath}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.WriteLine($"  ✗ Gemini error: {ex.Message}");
            return string.Empty;
        }
    }

    private async Task<string> ExtractPdfWithInlineDataAsync(string filePath, string apiKey)
    {
        // Read PDF file as base64
        var pdfBytes = await File.ReadAllBytesAsync(filePath);
        var pdfBase64 = Convert.ToBase64String(pdfBytes);
        
        // Prepare request payload
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
                            inlineData = new
                            {
                                mimeType = "application/pdf",
                                data = pdfBase64
                            }
                        },
                        new
                        {
                            text = "Extract all text from this PDF document. Return only the extracted text content, preserving the structure and formatting as much as possible."
                        }
                    }
                }
            }
        };
        
        var json = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        // Use global rate limiter to prevent conflicts with other services
        await GeminiRateLimiter.WaitForRateLimitAsync();
        
        // Make API request
        var url = $"{GeminiApiUrl}?key={apiKey}";
        
        var response = await _httpClient.PostAsync(url, content);
        var responseContent = await response.Content.ReadAsStringAsync();
        
        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"Gemini API error: {response.StatusCode} - {responseContent}");
            
            // Handle rate limiting (429) - retry with exponential backoff
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                Console.WriteLine($"  ⚠️ Rate limit exceeded. Retrying in 5 seconds...");
                await Task.Delay(5000); // Wait 5 seconds
                
                // Retry once
                try
                {
                    response = await _httpClient.PostAsync(url, content);
                    responseContent = await response.Content.ReadAsStringAsync();
                    
                    if (response.IsSuccessStatusCode)
                    {
                        return ParseGeminiResponse(responseContent);
                    }
                }
                catch (Exception retryEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Retry failed: {retryEx.Message}");
                }
                
                Console.WriteLine($"  ✗ Rate limit still exceeded. Please wait before processing more PDFs.");
                return string.Empty;
            }
            
            Console.WriteLine($"  ✗ Gemini API error: {response.StatusCode}");
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Console.WriteLine($"  ❌ Authentication failed - check API key in appsettings.json");
            }
            return string.Empty;
        }
        
        return ParseGeminiResponse(responseContent);
    }

    private async Task<string> ExtractPdfWithFileApiAsync(string filePath, string apiKey)
    {
        // Step 1: Upload file using File API
        var pdfBytes = await File.ReadAllBytesAsync(filePath);
        
        // Create multipart form data for file upload
        using var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        formData.Add(fileContent, "file", Path.GetFileName(filePath));
        
        // Use global rate limiter before file upload
        await GeminiRateLimiter.WaitForRateLimitAsync();
        
        // Upload file
        var uploadUrl = $"{GeminiFileApiUrl}?key={apiKey}";
        var uploadResponse = await _httpClient.PostAsync(uploadUrl, formData);
        var uploadResponseContent = await uploadResponse.Content.ReadAsStringAsync();
        
        if (!uploadResponse.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"File API upload error: {uploadResponse.StatusCode} - {uploadResponseContent}");
            
            // Handle rate limiting (429)
            if (uploadResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                Console.WriteLine($"  ⚠️ Rate limit exceeded during file upload. Please wait before processing more PDFs.");
                return string.Empty;
            }
            
            Console.WriteLine($"  ✗ File upload error: {uploadResponse.StatusCode}");
            return string.Empty;
        }
        
        // Parse upload response to get file URI
        var uploadResult = JsonConvert.DeserializeObject<dynamic>(uploadResponseContent);
        var fileUri = uploadResult?.file?.uri?.ToString();
        
        if (string.IsNullOrWhiteSpace(fileUri))
        {
            System.Diagnostics.Debug.WriteLine("File API upload succeeded but no file URI returned");
            Console.WriteLine($"  ✗ File upload succeeded but no URI returned");
            return string.Empty;
        }
        
        System.Diagnostics.Debug.WriteLine($"File uploaded successfully, URI: {fileUri}");
        Console.WriteLine($"  ✓ File uploaded to Gemini");
        
        // Step 2: Use the uploaded file in generateContent request
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
                            fileData = new
                            {
                                mimeType = "application/pdf",
                                fileUri = fileUri
                            }
                        },
                        new
                        {
                            text = "Extract all text from this PDF document. Return only the extracted text content, preserving the structure and formatting as much as possible."
                        }
                    }
                }
            }
        };
        
        var json = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        // Use global rate limiter before generateContent request
        await GeminiRateLimiter.WaitForRateLimitAsync();
        
        // Make generateContent request
        var generateUrl = $"{GeminiApiUrl}?key={apiKey}";
        
        var response = await _httpClient.PostAsync(generateUrl, content);
        var responseContent = await response.Content.ReadAsStringAsync();
        
        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"Gemini API error: {response.StatusCode} - {responseContent}");
            
            // Handle rate limiting (429) - retry with exponential backoff
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                Console.WriteLine($"  ⚠️ Rate limit exceeded. Retrying in 5 seconds...");
                await Task.Delay(5000); // Wait 5 seconds
                
                // Retry once
                try
                {
                    response = await _httpClient.PostAsync(generateUrl, content);
                    responseContent = await response.Content.ReadAsStringAsync();
                    
                    if (response.IsSuccessStatusCode)
                    {
                        return ParseGeminiResponse(responseContent);
                    }
                }
                catch (Exception retryEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Retry failed: {retryEx.Message}");
                }
                
                Console.WriteLine($"  ✗ Rate limit still exceeded. Please wait before processing more PDFs.");
                return string.Empty;
            }
            
            Console.WriteLine($"  ✗ Gemini API error: {response.StatusCode}");
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Console.WriteLine($"  ❌ Authentication failed - check API key in appsettings.json");
            }
            return string.Empty;
        }
        
        return ParseGeminiResponse(responseContent);
    }

    private string ParseGeminiResponse(string responseContent)
    {
        try
        {
            // Parse response
            var responseObj = JsonConvert.DeserializeObject<dynamic>(responseContent);
            
            // Check for errors first
            if (responseObj?.error != null)
            {
                var errorMessage = responseObj.error.message?.ToString() ?? "Unknown error";
                var errorCode = responseObj.error.code?.ToString() ?? "Unknown";
                System.Diagnostics.Debug.WriteLine($"Gemini API returned an error: {errorCode} - {errorMessage}");
                Console.WriteLine($"  ✗ Gemini API error: {errorCode} - {errorMessage}");
                return string.Empty;
            }
            
            // Try to extract text from the response
            var extractedText = responseObj?.candidates?[0]?.content?.parts?[0]?.text?.ToString() ?? string.Empty;
            
            if (!string.IsNullOrWhiteSpace(extractedText))
            {
                System.Diagnostics.Debug.WriteLine($"Successfully extracted {extractedText.Length} characters using Gemini");
                Console.WriteLine($"  ✓ Gemini extracted {extractedText.Length} characters");
                return extractedText.Trim();
            }
            else
            {
                // Log the response structure for debugging
                System.Diagnostics.Debug.WriteLine($"Gemini completed but no text was extracted");
                System.Diagnostics.Debug.WriteLine($"Response structure: {JsonConvert.SerializeObject(responseObj, Formatting.Indented)}");
                Console.WriteLine($"  ✗ Gemini returned no text");
                Console.WriteLine($"  📋 Response preview: {responseContent?.Substring(0, Math.Min(500, responseContent?.Length ?? 0))}...");
                return string.Empty;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error parsing Gemini response: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Response content: {responseContent}");
            Console.WriteLine($"  ✗ Error parsing Gemini response: {ex.Message}");
            return string.Empty;
        }
    }


    private string ExtractDocxText(string filePath)
    {
        try
        {
            using var wordDocument = WordprocessingDocument.Open(filePath, false);
            var body = wordDocument.MainDocumentPart?.Document?.Body;
            if (body == null) return string.Empty;

            var text = new StringBuilder();
            foreach (var paragraph in body.Elements<Paragraph>())
            {
                text.AppendLine(paragraph.InnerText);
            }
            return text.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ExtractXlsxText(string filePath)
    {
        try
        {
            using var spreadsheetDocument = SpreadsheetDocument.Open(filePath, false);
            var text = new StringBuilder();
            
            var workbookPart = spreadsheetDocument.WorkbookPart;
            if (workbookPart == null) return string.Empty;

            var sheets = workbookPart.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>();
            foreach (var sheet in sheets)
            {
                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
                var sheetData = worksheetPart.Worksheet.Elements<DocumentFormat.OpenXml.Spreadsheet.SheetData>().FirstOrDefault();
                
                if (sheetData != null)
                {
                    foreach (var row in sheetData.Elements<DocumentFormat.OpenXml.Spreadsheet.Row>())
                    {
                        var rowText = new List<string>();
                        foreach (var cell in row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>())
                        {
                            var cellValue = GetCellValue(cell, workbookPart);
                            if (!string.IsNullOrEmpty(cellValue))
                                rowText.Add(cellValue);
                        }
                        if (rowText.Count > 0)
                            text.AppendLine(string.Join(" | ", rowText));
                    }
                }
            }
            return text.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GetCellValue(DocumentFormat.OpenXml.Spreadsheet.Cell cell, DocumentFormat.OpenXml.Packaging.WorkbookPart workbookPart)
    {
        if (cell.CellValue == null) return string.Empty;
        
        var value = cell.CellValue.Text;
        if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString)
        {
            var stringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
            if (stringTable != null && int.TryParse(value, out int index))
            {
                return stringTable.ElementAt(index).InnerText;
            }
        }
        return value ?? string.Empty;
    }

    public List<string> ChunkText(string text, int chunkSize = 1000)
    {
        var chunks = new List<string>();
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        
        var currentChunk = new StringBuilder();
        foreach (var word in words)
        {
            if (currentChunk.Length + word.Length + 1 > chunkSize && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
            }
            if (currentChunk.Length > 0)
                currentChunk.Append(' ');
            currentChunk.Append(word);
        }
        
        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.ToString());
        
        return chunks;
    }

    public string ComputeFileHash(string filePath)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hash = md5.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

}
