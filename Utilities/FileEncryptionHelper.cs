using System;
using System.IO;

namespace FileWise.Utilities;

/// <summary>
/// Helper class to detect and distinguish between different types of file encryption:
/// 1. Windows EFS (Encrypting File System) - Works automatically if running under same user
/// 2. PDF password encryption - Requires password to decrypt
/// 3. WPS Office / Kingsoft Office encryption - Proprietary encryption used by WPS Office
/// </summary>
public static class FileEncryptionHelper
{
    /// <summary>
    /// Checks if a file is encrypted using Windows EFS (Encrypting File System).
    /// EFS-encrypted files are automatically decrypted by Windows when accessed by the same user account.
    /// </summary>
    /// <param name="filePath">Path to the file to check</param>
    /// <returns>True if the file is EFS-encrypted, false otherwise</returns>
    public static bool IsEfsEncrypted(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            var fileInfo = new FileInfo(filePath);
            var attributes = fileInfo.Attributes;
            
            // Check if the file has the Encrypted attribute (EFS)
            return (attributes & FileAttributes.Encrypted) == FileAttributes.Encrypted;
        }
        catch
        {
            // If we can't check, assume not EFS-encrypted
            return false;
        }
    }

    /// <summary>
    /// Checks if a PDF file has password encryption (different from EFS).
    /// This checks for PDF encryption markers in the file content.
    /// </summary>
    /// <param name="filePath">Path to the PDF file to check</param>
    /// <returns>True if the PDF has password encryption, false otherwise</returns>
    public static bool IsPdfPasswordEncrypted(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            var extension = Path.GetExtension(filePath).ToLower();
            if (extension != ".pdf")
                return false;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var buffer = new byte[Math.Min(8192, (int)new FileInfo(filePath).Length)];
                var bytesRead = fs.Read(buffer, 0, buffer.Length);
                var bufferStr = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead).ToLower();
                
                // Check for PDF encryption indicators
                if (bufferStr.Contains("/encrypt") || 
                    bufferStr.Contains("/encryption") ||
                    bufferStr.Contains("/filter/crypt") ||
                    bufferStr.Contains("/standardsecurityhandler") ||
                    bufferStr.Contains("/userpassword") ||
                    bufferStr.Contains("/ownerpassword"))
                {
                    return true;
                }
            }
        }
        catch
        {
            // If we can't read, we can't determine - return false to be safe
            return false;
        }
        
        return false;
    }

    /// <summary>
    /// Checks if a file is encrypted by WPS Office or Kingsoft Office.
    /// WPS Office uses proprietary encryption that may not follow standard PDF encryption patterns.
    /// </summary>
    /// <param name="filePath">Path to the file to check</param>
    /// <returns>True if the file appears to be WPS/Kingsoft Office encrypted, false otherwise</returns>
    public static bool IsWpsOfficeEncrypted(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                // Read a larger buffer to check for WPS Office signatures
                var buffer = new byte[Math.Min(16384, (int)new FileInfo(filePath).Length)];
                var bytesRead = fs.Read(buffer, 0, buffer.Length);
                
                // Check for WPS encryption hex signature: 17 DA 5F A0 (most reliable indicator)
                // This is the WPS Office encryption header signature
                bool hasWpsHexSignature = bytesRead >= 4 && 
                                         buffer[0] == 0x17 && 
                                         buffer[1] == 0xDA && 
                                         buffer[2] == 0x5F && 
                                         buffer[3] == 0xA0;
                
                if (hasWpsHexSignature)
                {
                    System.Diagnostics.Debug.WriteLine("WPS encryption detected via hex signature: 17 DA 5F A0");
                    return true;
                }
                
                // Also check for partial WPS signature (17 DA at start)
                bool hasPartialWpsSignature = bytesRead >= 2 && 
                                             buffer[0] == 0x17 && 
                                             buffer[1] == 0xDA;
                
                if (hasPartialWpsSignature)
                {
                    System.Diagnostics.Debug.WriteLine("WPS encryption detected via partial hex signature: 17 DA");
                    return true;
                }
                
                var bufferStr = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead).ToLower();
                
                // Check for WPS Office / Kingsoft Office indicators
                // WPS Office encrypted files may have these markers
                bool hasWpsMarker = bufferStr.Contains("wps") || 
                                   bufferStr.Contains("kingsoft") ||
                                   bufferStr.Contains("kingsoft office") ||
                                   bufferStr.Contains("wps office");
                
                // Check for WPS-specific encryption patterns
                // WPS Office may use non-standard encryption that makes PDFs unreadable
                bool hasWpsEncryptionPattern = bufferStr.Contains("/wps") ||
                                             bufferStr.Contains("/kingsoft") ||
                                             bufferStr.Contains("wpsencrypt") ||
                                             bufferStr.Contains("kingsoftencrypt");
                
                // Also check if file appears to be a PDF but has WPS markers and encryption indicators
                bool isPdf = bufferStr.Contains("%pdf") || bufferStr.StartsWith("%pdf");
                bool hasEncryptionIndicators = bufferStr.Contains("encrypt") || 
                                               bufferStr.Contains("password") ||
                                               bufferStr.Contains("security");
                
                // If it's a PDF with WPS markers and encryption, likely WPS-encrypted
                if (isPdf && hasWpsMarker && hasEncryptionIndicators)
                {
                    return true;
                }
                
                // If it has WPS encryption patterns
                if (hasWpsEncryptionPattern)
                {
                    return true;
                }
                
                // If it's not a valid PDF but has WPS markers, might be WPS-encrypted
                if (!isPdf && hasWpsMarker && bytesRead > 100)
                {
                    // Check if it looks like an encrypted/corrupted PDF
                    return true;
                }
            }
        }
        catch
        {
            // If we can't read, we can't determine
            return false;
        }
        
        return false;
    }

    /// <summary>
    /// Gets a description of the encryption type for a file.
    /// </summary>
    /// <param name="filePath">Path to the file to check</param>
    /// <returns>Description of encryption type, or null if not encrypted</returns>
    public static string? GetEncryptionDescription(string filePath)
    {
        bool isEfs = IsEfsEncrypted(filePath);
        bool isPdfPassword = IsPdfPasswordEncrypted(filePath);
        bool isWps = IsWpsOfficeEncrypted(filePath);

        if (isEfs && isPdfPassword && isWps)
        {
            return "Windows EFS + PDF Password + WPS Office Encryption (EFS works automatically, but PDF/WPS encryption requires password)";
        }
        else if (isEfs && isPdfPassword)
        {
            return "Windows EFS + PDF Password Encryption (EFS will work automatically, but PDF password is required)";
        }
        else if (isEfs && isWps)
        {
            return "Windows EFS + WPS Office Encryption (EFS works automatically, but WPS encryption requires WPS Office to decrypt)";
        }
        else if (isPdfPassword && isWps)
        {
            return "PDF Password + WPS Office Encryption (Requires password and/or WPS Office to decrypt)";
        }
        else if (isEfs)
        {
            return "Windows EFS (Encrypting File System) - Will work automatically if running under the same user account";
        }
        else if (isPdfPassword)
        {
            return "PDF Password Encryption - Requires password to decrypt";
        }
        else if (isWps)
        {
            return "WPS Office / Kingsoft Office Encryption - Requires WPS Office to decrypt or re-export";
        }

        return null;
    }

    /// <summary>
    /// Attempts to read a file to verify if it's accessible.
    /// For EFS-encrypted files, this will succeed if running under the same user account.
    /// </summary>
    /// <param name="filePath">Path to the file to test</param>
    /// <returns>True if the file can be read, false otherwise</returns>
    public static bool CanReadFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            // Try to open and read a small portion of the file
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var buffer = new byte[1];
                fs.Read(buffer, 0, 1);
            }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}

