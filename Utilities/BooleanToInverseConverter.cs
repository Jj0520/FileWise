using System;
using System.Globalization;
using System.Windows.Data;

namespace FileWise.Utilities;

public class BooleanToInverseConverter : IValueConverter
{
    public BooleanToInverseConverter()
    {
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            // Check for DependencyProperty.UnsetValue (indicates binding not resolved)
            if (value == null || value == System.Windows.DependencyProperty.UnsetValue)
                return true;

            if (value is bool boolValue)
                return !boolValue;

            // Try to parse as string if it's a string
            if (value is string stringValue && bool.TryParse(stringValue, out var parsedBool))
            {
                return !parsedBool;
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in BooleanToInverseConverter: {ex.Message}");
            return true;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value == null)
                return false;

            if (value is bool boolValue)
                return !boolValue;

            // Try to parse as string if it's a string
            if (value is string stringValue && bool.TryParse(stringValue, out var parsedBool))
            {
                return !parsedBool;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}

