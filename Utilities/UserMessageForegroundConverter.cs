using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using MediaBrush = System.Windows.Media.Brush;

namespace FileWise.Utilities;

public class UserMessageForegroundConverter : IValueConverter
{
    public UserMessageForegroundConverter()
    {
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            // Check for DependencyProperty.UnsetValue (indicates binding not resolved)
            if (value == null || value == System.Windows.DependencyProperty.UnsetValue)
                return Brushes.Black;

            bool isUser = false;
            if (value is bool b)
            {
                isUser = b;
            }
            else if (value is string stringValue && bool.TryParse(stringValue, out var parsedBool))
            {
                isUser = parsedBool;
            }
            else
            {
                return Brushes.Black;
            }

            var userBrush = GetBrushFromResources("ChatUserTextBrush", Brushes.White);
            var aiBrush = GetBrushFromResources("ChatAiTextBrush", Brushes.Black);
            var result = isUser ? userBrush : aiBrush;
            
            // If targetType is specified and it's a Brush type, ensure we return the right type
            if (targetType != null && typeof(System.Windows.Media.Brush).IsAssignableFrom(targetType))
            {
                return result;
            }
            
            return result;
        }
        catch (Exception ex)
        {
            // Return safe default on any error
            System.Diagnostics.Debug.WriteLine($"UserMessageForegroundConverter error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            return Brushes.Black;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static MediaBrush GetBrushFromResources(string key, MediaBrush fallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is MediaBrush brush)
        {
            return brush;
        }

        return fallback;
    }
}

