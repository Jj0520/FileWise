using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Collections;

namespace FileWise.Utilities;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value == null || value == DependencyProperty.UnsetValue)
                return Visibility.Collapsed;

            // If it's a collection, check if it has items
            if (value is ICollection collection)
            {
                return collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            // For other types, just check if not null
            return Visibility.Visible;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in NullToVisibilityConverter: {ex.Message}");
            return Visibility.Collapsed;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

