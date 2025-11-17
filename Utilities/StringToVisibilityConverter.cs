using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileWise.Utilities;

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string fileType && parameter is string targetFileType)
        {
            // Normalize both to lowercase for comparison
            var normalizedFileType = fileType.ToLowerInvariant().Trim();
            var normalizedTarget = targetFileType.ToLowerInvariant().Trim();
            
            // Ensure both start with dot
            if (!normalizedFileType.StartsWith("."))
                normalizedFileType = "." + normalizedFileType;
            if (!normalizedTarget.StartsWith("."))
                normalizedTarget = "." + normalizedTarget;
            
            return normalizedFileType.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

