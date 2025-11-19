using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace FileWise.Utilities;

public class NetworkPathDetectorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || value == DependencyProperty.UnsetValue)
            return false;

        if (value is string text)
        {
            var trimmedText = text.Trim();
            
            // Pattern to match network paths (\\server\share\path) - including paths with spaces
            var networkPathPattern = @"(\\\\[^\r\n]+)";
            var match = Regex.Match(trimmedText, networkPathPattern);
            
            if (match.Success)
            {
                var path = match.Value.Trim();
                // Ensure it's a valid network path (has at least \\server\share format)
                if (path.Length > 3 && path.Contains('\\') && path.Split('\\').Length >= 3)
                {
                    // Check if the paragraph is mostly just the network path (at least 80% of the text)
                    // This handles cases where the path is on its own line vs mixed with other text
                    var pathRatio = (double)path.Length / Math.Max(trimmedText.Length, 1);
                    if (pathRatio >= 0.5 || trimmedText.StartsWith("\\\\", StringComparison.Ordinal))
                    {
                        if (parameter?.ToString() == "Path")
                            return path;
                        return true; // IsNetworkPath
                    }
                }
            }
        }

        if (parameter?.ToString() == "Path")
            return value?.ToString() ?? string.Empty;
        return false; // IsNetworkPath
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

