using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace FileWise.Utilities;

public class TextToParagraphsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || value == DependencyProperty.UnsetValue)
            return new List<string> { string.Empty };

        if (value is string text)
        {
            // Normalize line endings
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // Split by double newlines (paragraph breaks)
            var paragraphs = text.Split(new[] { "\n\n", "\n \n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            
            // If no paragraphs found (no double newlines), split by single newlines for steps
            if (paragraphs.Count == 0 || (paragraphs.Count == 1 && text.Contains("\n")))
            {
                // Check if it looks like a step list (Step 1, Step 2, etc.)
                var lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
                
                var result = new List<string>();
                foreach (var line in lines)
                {
                    // If line starts with "Step", number, bullet, or common list markers, treat as new paragraph
                    var stepPattern = @"^(Step\s*\d+[:.]?|^\d+[\.)]|^[-•*]\s|^[a-zA-Z][\.)]\s)";
                    if (System.Text.RegularExpressions.Regex.IsMatch(line, stepPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        // Add empty paragraph before step if not first item (for spacing)
                        if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[result.Count - 1]))
                        {
                            result.Add(" "); // Space character for spacing (better than empty string)
                        }
                        result.Add(line);
                    }
                    else if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[result.Count - 1]))
                    {
                        // Append to last paragraph with a space (continuation of same paragraph)
                        result[result.Count - 1] += " " + line;
                    }
                    else
                    {
                        result.Add(line);
                    }
                }
                
                return result.Count > 0 ? result : new List<string> { text };
            }
            
            return paragraphs.Count > 0 ? paragraphs : new List<string> { text };
        }

        return new List<string> { value?.ToString() ?? string.Empty };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

