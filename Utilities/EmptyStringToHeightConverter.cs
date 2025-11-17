using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileWise.Utilities;

public class EmptyStringToHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str && (string.IsNullOrWhiteSpace(str) || str.Trim() == " "))
        {
            // Return height for spacing between paragraphs
            return 12.0; // 12 pixels of spacing between paragraphs
        }
        return Double.NaN; // Auto height for non-empty strings
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

