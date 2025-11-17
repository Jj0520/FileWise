using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileWise.Utilities;

public class BooleanToVisibilityConverter : IValueConverter
{
    public BooleanToVisibilityConverter()
    {
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            // Check for DependencyProperty.UnsetValue (indicates binding not resolved)
            if (value == null || value == System.Windows.DependencyProperty.UnsetValue)
                return Visibility.Collapsed;

            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }

            // Try to parse as string if it's a string
            if (value is string stringValue && bool.TryParse(stringValue, out var parsedBool))
            {
                return parsedBool ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in BooleanToVisibilityConverter: {ex.Message}");
            return Visibility.Collapsed;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value == null)
                return false;

            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}

