using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FileWise.Services;

/// <summary>
/// Service to interact with WPS Office COM automation for decrypting WPS-encrypted files.
/// Uses WPS Office's official COM interface to access decrypted content.
/// </summary>
public class WpsOfficeService
{
    // WPS Office COM ProgIDs (Kingsoft WPS uses "KWPS" prefix)
    private const string WpsProgId = "KWPS.Application"; // Primary: Kingsoft WPS Application
    private const string WpsProgId9 = "KWPS.Application.9"; // Version 9
    private const string WpsPdfProgId = "KWPS.PDF.9"; // WPS PDF handler
    private const string WpsDocumentProgId = "KWPS.Document.9"; // WPS Document
    private const string WpsDocumentProgId12 = "KWPS.Document.12"; // WPS Document version 12
    // Legacy/alternative ProgIDs (in case some versions use these)
    private const string WpsLegacyProgId = "WPS.Application"; // Legacy WPS Application
    private const string WpsWriterProgId = "WPS.Writer"; // Alternative: WPS Writer
    private const string WpsSpreadsheetProgId = "WPS.Spreadsheet"; // Alternative: WPS Spreadsheet
    private const string WpsPresentationProgId = "WPS.Presentation"; // Alternative: WPS Presentation

    /// <summary>
    /// Checks if WPS Office is installed and available via COM.
    /// </summary>
    /// <returns>True if WPS Office COM interface is available, false otherwise</returns>
    public static bool IsWpsOfficeAvailable()
    {
        try
        {
            Console.WriteLine("  🔍 Checking if WPS Office COM is available...");
            System.Diagnostics.Debug.WriteLine("Checking WPS Office COM availability");
            
            // Try all possible WPS COM ProgIDs (Kingsoft WPS uses "KWPS" prefix)
            string[] progIds = { 
                WpsProgId,           // KWPS.Application (most common)
                WpsProgId9,          // KWPS.Application.9
                WpsPdfProgId,        // KWPS.PDF.9 (for PDF files)
                WpsDocumentProgId,   // KWPS.Document.9
                WpsDocumentProgId12, // KWPS.Document.12
                WpsLegacyProgId,     // WPS.Application (legacy)
                WpsWriterProgId,     // WPS.Writer
                WpsSpreadsheetProgId, // WPS.Spreadsheet
                WpsPresentationProgId // WPS.Presentation
            };
            
            foreach (var progId in progIds)
            {
                try
                {
                    Console.WriteLine($"  🔍 Trying ProgID: {progId}...");
                    Type? wpsType = Type.GetTypeFromProgID(progId);
                    if (wpsType != null)
                    {
                        Console.WriteLine($"  ✓ Found WPS Office COM interface: {progId}");
                        try
                        {
                            var wpsApp = Activator.CreateInstance(wpsType);
                            if (wpsApp != null)
                            {
                                Console.WriteLine($"  ✓ Successfully created WPS Office COM object using: {progId}");
                                Marshal.ReleaseComObject(wpsApp);
                                return true;
                            }
                        }
                        catch (COMException comEx)
                        {
                            Console.WriteLine($"  ⚠️ Found {progId} but failed to create instance: {comEx.Message}");
                            Console.WriteLine($"  💡 This might mean WPS Office needs to be running first");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠️ ProgID '{progId}' not found");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️ Error checking ProgID '{progId}': {ex.Message}");
                }
            }
            
            Console.WriteLine("  ❌ None of the WPS Office COM ProgIDs were found");
            Console.WriteLine("  💡 Troubleshooting:");
            Console.WriteLine("     - Ensure WPS Office is installed");
            Console.WriteLine("     - Try opening WPS Office manually first (this registers COM interfaces)");
            Console.WriteLine("     - Check if WPS Office uses a different COM ProgID");
            Console.WriteLine("     - For enterprise WPS (192.168.255.254:6666), ensure WPS is logged in");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Error checking WPS Office: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Error checking WPS: {ex.Message}");
        }

        Console.WriteLine("  ❌ WPS Office COM is not available");
        return false;
    }

    /// <summary>
    /// Attempts to extract text from a WPS-encrypted file using WPS Office COM automation.
    /// </summary>
    /// <param name="filePath">Path to the WPS-encrypted file</param>
    /// <returns>Extracted text content, or null if extraction failed</returns>
    public static string? ExtractTextFromWpsFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"  ❌ File not found: {filePath}");
            return null;
        }

        Console.WriteLine($"  🔓 Attempting to extract text from WPS-encrypted file via COM...");
        Console.WriteLine($"  📄 File: {Path.GetFileName(filePath)}");
        
        // For PDF files, try using KWPS.PDF.9 directly first
        var extension = Path.GetExtension(filePath).ToLower();
        if (extension == ".pdf")
        {
            Console.WriteLine("  📄 PDF file detected, trying PDF-specific COM interface...");
            var pdfText = TryExtractFromPdfInterface(filePath);
            if (pdfText != null)
            {
                return pdfText;
            }
            Console.WriteLine("  ⚠️ PDF-specific interface failed, trying standard document interface...");
        }
        
        object? wpsApp = null;
        object? document = null;

        try
        {
            // Try to create WPS Application COM object
            // Try all possible WPS COM ProgIDs (Kingsoft WPS uses "KWPS" prefix)
            string[] progIds = { 
                WpsProgId,           // KWPS.Application (most common)
                WpsProgId9,          // KWPS.Application.9
                WpsPdfProgId,        // KWPS.PDF.9 (for PDF files)
                WpsDocumentProgId,   // KWPS.Document.9
                WpsDocumentProgId12, // KWPS.Document.12
                WpsLegacyProgId,     // WPS.Application (legacy)
                WpsWriterProgId,     // WPS.Writer
                WpsSpreadsheetProgId, // WPS.Spreadsheet
                WpsPresentationProgId // WPS.Presentation
            };
            
            Type? wpsType = null;
            foreach (var progId in progIds)
            {
                Console.WriteLine($"  🔍 Trying WPS COM ProgID: {progId}");
                wpsType = Type.GetTypeFromProgID(progId);
                if (wpsType != null)
                {
                    Console.WriteLine($"  ✓ Found WPS COM interface: {progId}");
                    break;
                }
            }

            if (wpsType == null)
            {
                Console.WriteLine("  ❌ WPS Office COM interface not found (tried all ProgIDs)");
                System.Diagnostics.Debug.WriteLine("WPS Office COM interface not found");
                return null;
            }

            Console.WriteLine("  🔧 Creating WPS Application COM object...");
            
            try
            {
                wpsApp = Activator.CreateInstance(wpsType);
                if (wpsApp == null)
                {
                    Console.WriteLine("  ❌ Failed to create WPS Office COM object");
                    System.Diagnostics.Debug.WriteLine("Failed to create WPS Office COM object");
                    return null;
                }
                Console.WriteLine("  ✓ WPS Application COM object created");
                
                // Try to check if WPS is logged in (if possible)
                try
                {
                    // Some WPS versions might have a UserName or Account property
                    // This is optional - if it fails, we continue anyway
                    var userNameProp = wpsType.InvokeMember("UserName",
                        System.Reflection.BindingFlags.GetProperty, null, wpsApp, null);
                    if (userNameProp != null)
                    {
                        Console.WriteLine($"  👤 WPS Office user: {userNameProp}");
                    }
                }
                catch
                {
                    // Not all WPS versions expose this - that's okay
                    Console.WriteLine("  💡 Note: Cannot check WPS login status (this is normal)");
                }
            }
            catch (COMException comEx)
            {
                Console.WriteLine($"  ❌ COM error creating WPS object: {comEx.Message}");
                Console.WriteLine($"  ❌ Error code: 0x{comEx.ErrorCode:X8}");
                Console.WriteLine("  💡 Try:");
                Console.WriteLine("     - Ensure WPS Office is installed");
                Console.WriteLine("     - Try opening WPS Office manually first");
                Console.WriteLine("     - Check if WPS Office COM is properly registered");
                throw;
            }

            // Get the Documents collection
            Console.WriteLine("  🔧 Getting WPS Documents collection...");
            var documentsProperty = wpsType.InvokeMember("Documents", 
                System.Reflection.BindingFlags.GetProperty, null, wpsApp, null);
            
            if (documentsProperty == null)
            {
                Console.WriteLine("  ❌ Failed to get WPS Documents collection");
                System.Diagnostics.Debug.WriteLine("Failed to get WPS Documents collection");
                return null;
            }
            Console.WriteLine("  ✓ Got WPS Documents collection");

            // Open the document (WPS will handle decryption if user is logged in)
            // Note: If files are encrypted with WPS server (192.168.255.254:6666), 
            // WPS Office must be logged into the account that has access to decrypt
            Console.WriteLine("  🔧 Opening document in WPS Office (WPS will handle decryption)...");
            Console.WriteLine($"  📄 Full file path: {filePath}");
            Console.WriteLine($"  📄 File exists: {File.Exists(filePath)}");
            Console.WriteLine("  💡 If file requires WPS server authentication, ensure WPS Office is logged in");
            
            try
            {
                // Try to open the document using WPS COM (invisible for indexing)
                Console.WriteLine("  🔧 Attempting to open document via WPS COM (invisible)...");
                var openMethod = documentsProperty.GetType().InvokeMember("Open",
                    System.Reflection.BindingFlags.InvokeMethod, null, documentsProperty,
                    new object[] { filePath, false, false }); // ReadOnly=false, Visible=false

                document = openMethod;

                if (document == null)
                {
                    Console.WriteLine("  ❌ Documents.Open() returned null");
                    Console.WriteLine("  💡 This may require administrator permissions or WPS Office configuration");
                    Console.WriteLine("  💡 Contact your administrator to allow this application to access WPS-encrypted files");
                    System.Diagnostics.Debug.WriteLine("Failed to open document in WPS Office - Open() returned null");
                    return null;
                }
                Console.WriteLine("  ✓ Document opened in WPS Office");
            }
            catch (COMException comEx)
            {
                Console.WriteLine($"  ❌ COM error opening document: {comEx.Message}");
                Console.WriteLine($"  ❌ Error code: 0x{comEx.ErrorCode:X8}");
                Console.WriteLine($"  ❌ HRESULT: 0x{comEx.ErrorCode:X8}");
                Console.WriteLine($"  ❌ Stack trace: {comEx.StackTrace}");
                Console.WriteLine("  💡 This might indicate:");
                Console.WriteLine("     - Authentication required (WPS Office not logged in)");
                Console.WriteLine("     - Server connection issue (if using WPS server at 192.168.255.254:6666)");
                Console.WriteLine("     - File access permission issue");
                Console.WriteLine("     - File is locked or in use");
                Console.WriteLine("     - File format not supported by this WPS version");
                System.Diagnostics.Debug.WriteLine($"COM error details: {comEx}");
                // Don't re-throw, return null instead
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ General error opening document: {ex.GetType().Name}");
                Console.WriteLine($"  ❌ Error message: {ex.Message}");
                Console.WriteLine($"  ❌ Stack trace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"Error opening document: {ex}");
                return null;
            }

            // Extract text content
            Console.WriteLine("  🔧 Extracting text content from document...");
            string? text = null;
            
            // Try different methods to get text depending on document type
            try
            {
                // Method 1: Try Content property (for Writer documents)
                Console.WriteLine("  🔍 Trying Content.Text property...");
                var contentProperty = document.GetType().InvokeMember("Content",
                    System.Reflection.BindingFlags.GetProperty, null, document, null);
                
                if (contentProperty != null)
                {
                    var textProperty = contentProperty.GetType().InvokeMember("Text",
                        System.Reflection.BindingFlags.GetProperty, null, contentProperty, null);
                    text = textProperty?.ToString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        Console.WriteLine($"  ✓ Successfully extracted {text.Length} characters via Content.Text");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️ Content.Text method failed: {ex.Message}");
                // Try alternative method
            }

            // Method 2: Try Range property
            if (string.IsNullOrEmpty(text))
            {
                try
                {
                    Console.WriteLine("  🔍 Trying Range.Text property...");
                    var rangeProperty = document.GetType().InvokeMember("Range",
                        System.Reflection.BindingFlags.GetProperty, null, document, null);
                    
                    if (rangeProperty != null)
                    {
                        var textProperty = rangeProperty.GetType().InvokeMember("Text",
                            System.Reflection.BindingFlags.GetProperty, null, rangeProperty, null);
                        text = textProperty?.ToString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            Console.WriteLine($"  ✓ Successfully extracted {text.Length} characters via Range.Text");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️ Range.Text method failed: {ex.Message}");
                    // Try next method
                }
            }

            // Method 3: Try SaveAs to extract text (for PDFs opened in WPS)
            if (string.IsNullOrEmpty(text))
            {
                try
                {
                    // For PDFs, WPS might need different handling
                    // Try to get text from the document object directly
                    var fullNameProperty = document.GetType().InvokeMember("FullName",
                        System.Reflection.BindingFlags.GetProperty, null, document, null);
                    
                    // If it's a PDF, we might need to use WPS's PDF text extraction
                    // This is a fallback - the main extraction should work for WPS documents
                }
                catch
                {
                    // Continue
                }
            }

            // Close the document
            Console.WriteLine("  🔧 Closing document...");
            try
            {
                var closeMethod = document.GetType().InvokeMember("Close",
                    System.Reflection.BindingFlags.InvokeMethod, null, document, 
                    new object[] { false }); // Don't save changes
                Console.WriteLine("  ✓ Document closed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️ Error closing document: {ex.Message}");
                // Ignore close errors
            }

            if (string.IsNullOrEmpty(text))
            {
                Console.WriteLine("  ❌ No text extracted from document");
            }
            else
            {
                Console.WriteLine($"  ✓ Successfully extracted {text.Length} characters total");
            }

            return text;
        }
        catch (COMException comEx)
        {
            Console.WriteLine($"  ❌ WPS Office COM error: {comEx.Message}");
            Console.WriteLine($"  ❌ Error code: 0x{comEx.ErrorCode:X8}");
            System.Diagnostics.Debug.WriteLine($"WPS Office COM error: {comEx.Message} (0x{comEx.ErrorCode:X8})");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Error extracting text from WPS file: {ex.Message}");
            Console.WriteLine($"  ❌ Exception type: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"Error extracting text from WPS file: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            return null;
        }
        finally
        {
            // Clean up COM objects
            if (document != null)
            {
                try
                {
                    Marshal.ReleaseComObject(document);
                }
                catch { }
            }

            if (wpsApp != null)
            {
                try
                {
                    // Quit WPS if we started it
                    try
                    {
                        var quitMethod = wpsApp.GetType().InvokeMember("Quit",
                            System.Reflection.BindingFlags.InvokeMethod, null, wpsApp, 
                            new object[] { false }); // Don't save changes
                    }
                    catch { }
                    
                    Marshal.ReleaseComObject(wpsApp);
                }
                catch { }
            }

            // Force garbage collection to release COM objects
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// Attempts to convert a WPS-encrypted PDF to an unencrypted PDF using WPS Office.
    /// </summary>
    /// <param name="inputPath">Path to the encrypted PDF</param>
    /// <param name="outputPath">Path where the unencrypted PDF should be saved</param>
    /// <returns>True if conversion succeeded, false otherwise</returns>
    public static bool ConvertWpsPdfToUnencrypted(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"  ❌ Input file not found: {inputPath}");
            return false;
        }

        Console.WriteLine($"  🔓 Attempting to convert WPS-encrypted PDF to unencrypted...");
        Console.WriteLine($"  📥 Input: {Path.GetFileName(inputPath)}");
        Console.WriteLine($"  📤 Output: {Path.GetFileName(outputPath)}");

        object? wpsApp = null;
        object? document = null;

        try
        {
            // Try to create WPS Application COM object
            // Try all possible WPS COM ProgIDs (Kingsoft WPS uses "KWPS" prefix)
            string[] progIds = { 
                WpsProgId,           // KWPS.Application (most common)
                WpsProgId9,          // KWPS.Application.9
                WpsPdfProgId,        // KWPS.PDF.9 (for PDF files)
                WpsDocumentProgId,   // KWPS.Document.9
                WpsDocumentProgId12, // KWPS.Document.12
                WpsLegacyProgId,     // WPS.Application (legacy)
                WpsWriterProgId      // WPS.Writer
            };
            
            Console.WriteLine("  🔧 Creating WPS Application COM object...");
            Type? wpsType = null;
            foreach (var progId in progIds)
            {
                Console.WriteLine($"  🔍 Trying ProgID: {progId}");
                wpsType = Type.GetTypeFromProgID(progId);
                if (wpsType != null)
                {
                    Console.WriteLine($"  ✓ Found WPS COM interface: {progId}");
                    break;
                }
            }

            if (wpsType == null)
            {
                Console.WriteLine("  ❌ WPS Office COM interface not found (tried all ProgIDs)");
                System.Diagnostics.Debug.WriteLine("WPS Office COM interface not found");
                return false;
            }

            wpsApp = Activator.CreateInstance(wpsType);
            if (wpsApp == null)
            {
                Console.WriteLine("  ❌ Failed to create WPS Application COM object");
                return false;
            }
            Console.WriteLine("  ✓ WPS Application COM object created");

            // Get the Documents collection
            Console.WriteLine("  🔧 Getting WPS Documents collection...");
            var documentsProperty = wpsType.InvokeMember("Documents",
                System.Reflection.BindingFlags.GetProperty, null, wpsApp, null);

            if (documentsProperty == null)
            {
                Console.WriteLine("  ❌ Failed to get WPS Documents collection");
                return false;
            }
            Console.WriteLine("  ✓ Got WPS Documents collection");

            // Open the encrypted PDF
            Console.WriteLine("  🔧 Opening encrypted PDF in WPS Office...");
            var openMethod = documentsProperty.GetType().InvokeMember("Open",
                System.Reflection.BindingFlags.InvokeMethod, null, documentsProperty,
                new object[] { inputPath, false, false }); // ReadOnly, Not Visible

            document = openMethod;

            if (document == null)
            {
                Console.WriteLine("  ❌ Failed to open document in WPS Office (Documents.Open() returned null)");
                Console.WriteLine("  💡 This may require administrator permissions or WPS Office configuration");
                Console.WriteLine("  💡 Contact your administrator to allow this application to access WPS-encrypted files");
                return false;
            }
            Console.WriteLine("  ✓ Document opened in WPS Office");

            // Save as unencrypted PDF
            Console.WriteLine("  🔧 Saving as unencrypted PDF...");
            var saveAsMethod = document.GetType().InvokeMember("SaveAs",
                System.Reflection.BindingFlags.InvokeMethod, null, document,
                new object[] { outputPath, 17 }); // 17 = PDF format (wdFormatPDF)
            Console.WriteLine("  ✓ SaveAs completed");

            // Close the document
            Console.WriteLine("  🔧 Closing document...");
            var closeMethod = document.GetType().InvokeMember("Close",
                System.Reflection.BindingFlags.InvokeMethod, null, document,
                new object[] { false });
            Console.WriteLine("  ✓ Document closed");

            bool success = File.Exists(outputPath);
            if (success)
            {
                var fileInfo = new FileInfo(outputPath);
                Console.WriteLine($"  ✓ Conversion successful! Output file: {fileInfo.Length / 1024.0:F2} KB");
            }
            else
            {
                Console.WriteLine("  ❌ Conversion failed - output file not found");
            }

            return success;
        }
        catch (COMException comEx)
        {
            Console.WriteLine($"  ❌ WPS Office COM error during conversion: {comEx.Message}");
            Console.WriteLine($"  ❌ Error code: 0x{comEx.ErrorCode:X8}");
            System.Diagnostics.Debug.WriteLine($"WPS COM error: {comEx.Message} (0x{comEx.ErrorCode:X8})");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Error converting WPS PDF: {ex.Message}");
            Console.WriteLine($"  ❌ Exception type: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"Error converting WPS PDF: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
        finally
        {
            // Clean up COM objects
            if (document != null)
            {
                try { Marshal.ReleaseComObject(document); } catch { }
            }

            if (wpsApp != null)
            {
                try
                {
                    var quitMethod = wpsApp.GetType().InvokeMember("Quit",
                        System.Reflection.BindingFlags.InvokeMethod, null, wpsApp,
                        new object[] { false });
                }
                catch { }
                
                try { Marshal.ReleaseComObject(wpsApp); } catch { }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// Attempts to extract text from a PDF file using WPS PDF-specific COM interface.
    /// </summary>
    private static string? TryExtractFromPdfInterface(string filePath)
    {
        object? pdfApp = null;
        object? pdfDoc = null;
        
        try
        {
            Console.WriteLine("  🔍 Trying KWPS.PDF.9 COM interface for PDF...");
            Type? pdfType = Type.GetTypeFromProgID(WpsPdfProgId);
            if (pdfType == null)
            {
                Console.WriteLine($"  ⚠️ {WpsPdfProgId} not found, trying KWPS.Document.9...");
                pdfType = Type.GetTypeFromProgID(WpsDocumentProgId);
            }
            
            if (pdfType == null)
            {
                Console.WriteLine("  ⚠️ PDF-specific COM interfaces not found");
                return null;
            }
            
            Console.WriteLine($"  ✓ Found PDF COM interface: {pdfType.Name}");
            pdfApp = Activator.CreateInstance(pdfType);
            if (pdfApp == null)
            {
                Console.WriteLine("  ❌ Failed to create PDF COM object");
                return null;
            }
            Console.WriteLine("  ✓ PDF COM object created");
            
            // Try to get Documents or similar collection
            try
            {
                var documentsProperty = pdfType.InvokeMember("Documents", 
                    System.Reflection.BindingFlags.GetProperty, null, pdfApp, null);
                
                if (documentsProperty != null)
                {
                    Console.WriteLine("  ✓ Got PDF Documents collection");
                    
                    // Try opening with various signatures
                    var openSignatures = new[]
                    {
                        new object[] { filePath },
                        new object[] { filePath, false },
                        new object[] { filePath, false, true },
                        new object[] { filePath, true, false, false }
                    };
                    
                    foreach (var sig in openSignatures)
                    {
                        try
                        {
                            var sigStr = string.Join(", ", sig.Select(s => s?.ToString() ?? "null"));
                            Console.WriteLine($"  🔍 Trying Open({sigStr})");
                            var openResult = documentsProperty.GetType().InvokeMember("Open",
                                System.Reflection.BindingFlags.InvokeMethod, null, documentsProperty, sig);
                            
                            if (openResult != null)
                            {
                                pdfDoc = openResult;
                                Console.WriteLine("  ✓ PDF document opened successfully!");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ⚠️ Open() signature failed: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️ Could not access Documents property: {ex.Message}");
            }
            
            if (pdfDoc == null)
            {
                Console.WriteLine("  ❌ Could not open PDF document via PDF interface");
                return null;
            }
            
            // Try to extract text
            Console.WriteLine("  🔧 Attempting to extract text from PDF document...");
            try
            {
                var contentProperty = pdfDoc.GetType().InvokeMember("Content",
                    System.Reflection.BindingFlags.GetProperty, null, pdfDoc, null);
                
                if (contentProperty != null)
                {
                    var textProperty = contentProperty.GetType().InvokeMember("Text",
                        System.Reflection.BindingFlags.GetProperty, null, contentProperty, null);
                    
                    if (textProperty is string text && !string.IsNullOrWhiteSpace(text))
                    {
                        Console.WriteLine($"  ✓ Extracted {text.Length} characters from PDF");
                        return text;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️ Could not extract text via Content.Text: {ex.Message}");
            }
            
            // Try Range.Text as alternative
            try
            {
                var rangeProperty = pdfDoc.GetType().InvokeMember("Range",
                    System.Reflection.BindingFlags.GetProperty, null, pdfDoc, null);
                
                if (rangeProperty != null)
                {
                    var textProperty = rangeProperty.GetType().InvokeMember("Text",
                        System.Reflection.BindingFlags.GetProperty, null, rangeProperty, null);
                    
                    if (textProperty is string text && !string.IsNullOrWhiteSpace(text))
                    {
                        Console.WriteLine($"  ✓ Extracted {text.Length} characters from PDF via Range.Text");
                        return text;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️ Could not extract text via Range.Text: {ex.Message}");
            }
            
            Console.WriteLine("  ❌ Could not extract text from PDF document");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Error using PDF interface: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            // Cleanup
            try
            {
                if (pdfDoc != null)
                {
                    var closeMethod = pdfDoc.GetType().InvokeMember("Close",
                        System.Reflection.BindingFlags.InvokeMethod, null, pdfDoc, 
                        new object[] { false });
                }
            }
            catch { }
            
            try
            {
                if (pdfApp != null)
                {
                    var quitMethod = pdfApp.GetType().InvokeMember("Quit",
                        System.Reflection.BindingFlags.InvokeMethod, null, pdfApp, null);
                    Marshal.ReleaseComObject(pdfApp);
                }
            }
            catch { }
        }
    }
}

