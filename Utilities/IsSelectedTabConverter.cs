using System;
using System.Globalization;
using System.Windows.Data;
using FileWise.Models;

namespace FileWise.Utilities;

public class IsSelectedTabConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (values == null || values.Length < 2)
                return false;

            // Check for DependencyProperty.UnsetValue (indicates binding not resolved)
            if (values[0] == System.Windows.DependencyProperty.UnsetValue || 
                values[1] == System.Windows.DependencyProperty.UnsetValue)
                return false;

            var selectedTab = values[0] as ChatTab;
            var currentTab = values[1] as ChatTab;

            if (selectedTab == null || currentTab == null)
                return false;

            // Check for null Id to prevent NullReferenceException
            if (string.IsNullOrEmpty(selectedTab.Id) || string.IsNullOrEmpty(currentTab.Id))
                return false;

            return selectedTab.Id == currentTab.Id;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in IsSelectedTabConverter: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

