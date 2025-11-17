using System;
using System.Globalization;
using System.Windows.Data;
using FileWise.Models;

namespace FileWise.Utilities;

public class IsSelectedFileConverter : IMultiValueConverter
{
    public IsSelectedFileConverter()
    {
    }

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

            var currentFile = values[0] as FileMetadata;
            var selectedFile = values[1] as FileMetadata;

            if (currentFile == null || selectedFile == null)
                return false;

            return currentFile == selectedFile || 
                   (currentFile.FilePath != null && selectedFile.FilePath != null && 
                    currentFile.FilePath == selectedFile.FilePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in IsSelectedFileConverter: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

