using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace FileWise.Utilities;

public static class TextWithHyperlinksHelper
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(TextWithHyperlinksHelper),
            new PropertyMetadata(null, OnTextChanged));

    public static string GetText(DependencyObject obj)
    {
        return (string)obj.GetValue(TextProperty);
    }

    public static void SetText(DependencyObject obj, string value)
    {
        obj.SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            var text = e.NewValue as string;
            if (string.IsNullOrEmpty(text))
            {
                textBlock.Inlines.Clear();
                textBlock.Text = string.Empty;
                return;
            }

            textBlock.Inlines.Clear();

            // Pattern to match network paths (\\server\share\path) - including paths with spaces
            // Matches \\ followed by any characters (including spaces) until end of line or next line break
            var networkPathPattern = @"(\\\\[^\r\n]+)";
            var allMatches = Regex.Matches(text, networkPathPattern);
            
            // Filter matches to only include valid network paths (at least \\server\share)
            var validMatches = new List<Match>();
            foreach (Match match in allMatches)
            {
                var path = match.Value.Trim();
                // Ensure it's a valid network path (has at least \\server\share format)
                if (path.Length > 3 && path.Contains('\\') && path.Split('\\').Length >= 3)
                {
                    validMatches.Add(match);
                }
            }

            if (validMatches.Count == 0)
            {
                // No network paths found, just set text
                textBlock.Text = text;
                return;
            }

            // Split text by network paths and create hyperlinks
            int lastIndex = 0;
            foreach (Match match in validMatches)
            {
                // Add text before the path
                if (match.Index > lastIndex)
                {
                    textBlock.Inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));
                }

                // Add hyperlink for the network path
                var pathValue = match.Value.TrimEnd('.', ' ', '\r', '\n');
                var hyperlink = new Hyperlink(new Run(pathValue));
                // Inherit foreground color from parent TextBlock
                hyperlink.Foreground = textBlock.Foreground;
                hyperlink.ToolTip = $"Click to open folder: {pathValue}";
                hyperlink.Click += (s, args) =>
                {
                    try
                    {
                        // Open the network path as a folder in Windows Explorer
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{pathValue}\"",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error opening folder: {ex.Message}", "Error", 
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                };
                textBlock.Inlines.Add(hyperlink);

                lastIndex = match.Index + match.Length;
            }

            // Add remaining text after the last path
            if (lastIndex < text.Length)
            {
                textBlock.Inlines.Add(new Run(text.Substring(lastIndex)));
            }
        }
    }
}

