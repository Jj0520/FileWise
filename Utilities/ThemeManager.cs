using System;
using System.Windows;
using Microsoft.Win32;

namespace FileWise.Utilities;

public enum ThemePreference
{
    Light,
    Dark,
    System
}

public static class ThemeManager
{
    private const string LightThemeUri = "Themes/LightTheme.xaml";
    private const string DarkThemeUri = "Themes/DarkTheme.xaml";
    
    private static ResourceDictionary? _currentDictionary;
    private static ThemePreference _currentPreference = ThemePreference.System;
    private static bool _isSubscribed;

    static ThemeManager()
    {
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        _isSubscribed = true;
    }

    private static void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_currentPreference != ThemePreference.System)
        {
            return;
        }

        if (System.Windows.Application.Current == null)
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            ApplyTheme(ThemePreference.System);
        });
    }

    public static ThemePreference CurrentPreference => _currentPreference;

    public static ThemePreference ParsePreference(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "light" => ThemePreference.Light,
            "dark" => ThemePreference.Dark,
            "system" => ThemePreference.System,
            _ => ThemePreference.System
        };
    }

    public static void ApplyTheme(ThemePreference preference)
    {
        _currentPreference = preference;

        var effectiveTheme = preference == ThemePreference.System
            ? DetectSystemTheme()
            : preference;

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(effectiveTheme == ThemePreference.Dark ? DarkThemeUri : LightThemeUri, UriKind.Relative)
        };

        var application = System.Windows.Application.Current;
        if (application == null)
        {
            return;
        }

        var mergedDictionaries = application.Resources.MergedDictionaries;

        // Remove previous dictionary if present
        if (_currentDictionary != null)
        {
            mergedDictionaries.Remove(_currentDictionary);
        }

        mergedDictionaries.Add(dictionary);
        _currentDictionary = dictionary;
    }

    public static void Dispose()
    {
        if (_isSubscribed)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            _isSubscribed = false;
        }
    }

    private static ThemePreference DetectSystemTheme()
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (personalizeKey != null)
            {
                var appsUseLightTheme = personalizeKey.GetValue("AppsUseLightTheme");
                if (appsUseLightTheme is int intValue)
                {
                    return intValue == 0 ? ThemePreference.Dark : ThemePreference.Light;
                }
            }
        }
        catch
        {
            // Ignore registry access issues and fall back to light theme.
        }

        return ThemePreference.Light;
    }
}

