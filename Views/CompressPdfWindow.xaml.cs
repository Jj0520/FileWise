using System;
using System.Windows;

namespace FileWise.Views;

public partial class CompressPdfWindow : Window
{
    public CompressPdfWindow()
    {
        InitializeComponent();
    }

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Implement file selection dialog
        System.Diagnostics.Debug.WriteLine("AddFilesButton_Click - Not yet implemented");
    }

    private void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Implement remove selected files
        System.Diagnostics.Debug.WriteLine("RemoveSelectedButton_Click - Not yet implemented");
    }

    private void CompressButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Implement PDF compression
        System.Diagnostics.Debug.WriteLine("CompressButton_Click - Not yet implemented");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}


