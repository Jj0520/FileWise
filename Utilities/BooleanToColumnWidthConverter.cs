using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileWise.Utilities;

public class BooleanToColumnWidthConverter : IValueConverter
{
    public BooleanToColumnWidthConverter()
    {
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            // Check for DependencyProperty.UnsetValue (indicates binding not resolved)
            if (value == null || value == System.Windows.DependencyProperty.UnsetValue)
                return new GridLength(280);

            bool boolValue = false;
            if (value is bool b)
            {
                boolValue = b;
            }
            else if (value is string stringValue && bool.TryParse(stringValue, out var parsedBool))
            {
                boolValue = parsedBool;
            }
            else
            {
                return new GridLength(280);
            }

            // If true, return the width (default 280), if false return 0
            var width = parameter != null && double.TryParse(parameter.ToString(), out var parsedWidth) 
                ? parsedWidth 
                : 280.0;
            return boolValue ? new GridLength(width) : new GridLength(0);
        }
        catch (Exception ex)
        {
            // Return safe default on any error
            System.Diagnostics.Debug.WriteLine($"Error in BooleanToColumnWidthConverter: {ex.Message}");
            return new GridLength(280);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

