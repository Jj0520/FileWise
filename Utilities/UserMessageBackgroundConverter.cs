using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.Brush;

namespace FileWise.Utilities;

public class UserMessageBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush UserBrush;
    private static readonly SolidColorBrush DefaultBrush;

    static UserMessageBackgroundConverter()
    {
        // User messages: Primary blue from new palette (#2A93D5 = RGB 42, 147, 213)
        UserBrush = new SolidColorBrush(Color.FromRgb(42, 147, 213));
        // AI messages: Light gray
        DefaultBrush = new SolidColorBrush(Color.FromRgb(243, 244, 246));
        UserBrush.Freeze();
        DefaultBrush.Freeze();
    }

    public UserMessageBackgroundConverter()
    {
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            // Check for DependencyProperty.UnsetValue (indicates binding not resolved)
            if (value == null || value == System.Windows.DependencyProperty.UnsetValue)
                return DefaultBrush;

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
                return DefaultBrush;
            }

            var userBrush = GetBrushFromResources("ChatUserMessageBrush", UserBrush);
            var aiBrush = GetBrushFromResources("ChatAiMessageBrush", DefaultBrush);

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
            System.Diagnostics.Debug.WriteLine($"UserMessageBackgroundConverter error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            return DefaultBrush;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static MediaBrush GetBrushFromResources(string resourceKey, MediaBrush fallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(resourceKey) is MediaBrush brush)
        {
            return brush;
        }

        return fallback;
    }
}

