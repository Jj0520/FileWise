using System;
using System.Globalization;
using System.Windows.Data;

namespace FileWise.Utilities;

public class FileSizeConverter : IValueConverter
{
    public FileSizeConverter()
    {
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            // Check for DependencyProperty.UnsetValue (indicates binding not resolved)
            if (value == null || value == System.Windows.DependencyProperty.UnsetValue)
                return "0 B";

            long size = 0;
            if (value is long l)
            {
                size = l;
            }
            else if (value is int i)
            {
                size = i;
            }
            else if (value is string stringValue && long.TryParse(stringValue, out var parsedLong))
            {
                size = parsedLong;
            }
            else
            {
                return "0 B";
            }

            if (size < 0)
                return "0 B";

            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = size;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in FileSizeConverter: {ex.Message}");
            return "0 B";
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}






