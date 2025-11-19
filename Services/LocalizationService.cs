using System;
using System.Globalization;
using System.Resources;
using System.Reflection;

namespace FileWise.Services;

public class LocalizationService
{
    private static LocalizationService? _instance;
    private ResourceManager? _resourceManager;
    private CultureInfo _currentCulture;

    public event EventHandler? LanguageChanged;

    private LocalizationService()
    {
        _currentCulture = CultureInfo.GetCultureInfo("en-US");
        LoadResources();
    }

    public static LocalizationService Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new LocalizationService();
            }
            return _instance;
        }
    }

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture.Name != value.Name)
            {
                _currentCulture = value;
                LoadResources();
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string GetString(string key)
    {
        try
        {
            if (_resourceManager != null)
            {
                var value = _resourceManager.GetString(key, _currentCulture);
                return value ?? key; // Return key if translation not found
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting localized string for key '{key}': {ex.Message}");
        }
        return key; // Fallback to key
    }

    private void LoadResources()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            _resourceManager = new ResourceManager("FileWise.Resources.Strings", assembly);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading resources: {ex.Message}");
        }
    }

    public void SetLanguage(string languageCode)
    {
        try
        {
            CurrentCulture = CultureInfo.GetCultureInfo(languageCode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting language to '{languageCode}': {ex.Message}");
            CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        }
    }
}


