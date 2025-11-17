using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileWise.Utilities;

public class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Show empty strings (for spacing) but hide null
        if (value == null || value == DependencyProperty.UnsetValue)
            return Visibility.Collapsed;
        
        if (value is string str)
        {
            // Show empty strings as visible (they'll be used for spacing)
            return Visibility.Visible;
        }
        
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}






