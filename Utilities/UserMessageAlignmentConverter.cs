using System;
using System.Globalization;
using System.Windows.Data;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace FileWise.Utilities;

public class UserMessageAlignmentConverter : IValueConverter
{
    public UserMessageAlignmentConverter()
    {
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            // Check for DependencyProperty.UnsetValue (indicates binding not resolved)
            if (value == null || value == System.Windows.DependencyProperty.UnsetValue)
                return HorizontalAlignment.Left;

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
                return HorizontalAlignment.Left;
            }

            var result = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            
            // Ensure we return the correct enum type
            if (targetType != null && targetType.IsEnum)
            {
                return Enum.ToObject(targetType, (int)result);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            // Return safe default on any error
            System.Diagnostics.Debug.WriteLine($"UserMessageAlignmentConverter error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            return HorizontalAlignment.Left;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

