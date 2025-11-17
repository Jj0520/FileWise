using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;

namespace FileWise.Utilities;

public class NewlineToLineBreakConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || value == DependencyProperty.UnsetValue)
            return string.Empty;

        if (value is string text)
        {
            // Normalize line endings
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // Replace double newlines (paragraph breaks) with a special marker
            // Then replace single newlines with spaces (within paragraphs)
            // Then replace the marker with double newlines for display
            text = text.Replace("\n\n", "\u2029\u2029"); // Use paragraph separator as marker
            text = text.Replace("\n", " "); // Single newlines become spaces
            text = text.Replace("\u2029\u2029", "\n\n"); // Restore paragraph breaks
            
            // Clean up multiple spaces
            while (text.Contains("  "))
            {
                text = text.Replace("  ", " ");
            }
            
            return text;
        }

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

