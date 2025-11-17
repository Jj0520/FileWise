using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using FileWise.ViewModels;

namespace FileWise.Utilities;

public class ViewModeToVisibilityConverter : IValueConverter
{
    public ViewModeToVisibilityConverter()
    {
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            // Check for DependencyProperty.UnsetValue (indicates binding not resolved)
            if (value == null || value == System.Windows.DependencyProperty.UnsetValue)
                return Visibility.Collapsed;

            if (parameter == null)
                return Visibility.Collapsed;

            if (value is ViewMode currentMode)
            {
                var targetMode = parameter.ToString();
                return currentMode.ToString() == targetMode ? Visibility.Visible : Visibility.Collapsed;
            }

            // Try to parse as string if it's a string
            if (value is string stringValue && Enum.TryParse<ViewMode>(stringValue, out var parsedMode))
            {
                var targetMode = parameter.ToString();
                return parsedMode.ToString() == targetMode ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            // Return safe default on any error
            System.Diagnostics.Debug.WriteLine($"Error in ViewModeToVisibilityConverter: {ex.Message}");
            return Visibility.Collapsed;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}






