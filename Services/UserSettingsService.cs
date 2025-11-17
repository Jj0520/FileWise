using System;
using System.IO;
using System.Text.Json;

namespace FileWise.Services;

public class UserSettingsService
{
    private readonly string _settingsFilePath;
    private UserSettings _settings;

    public UserSettingsService()
    {
        try
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FileWise");
            
            if (!Directory.Exists(appDataPath))
            {
                try
                {
                    Directory.CreateDirectory(appDataPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating settings directory: {ex.Message}");
                    // Continue with default settings if directory creation fails
                }
            }

            _settingsFilePath = Path.Combine(appDataPath, "usersettings.json");
            _settings = LoadSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing UserSettingsService: {ex.Message}");
            // Initialize with default settings if anything fails
            _settings = new UserSettings();
            _settingsFilePath = string.Empty;
        }
    }

    public string SelectedFolder
    {
        get => _settings.SelectedFolder ?? string.Empty;
        set
        {
            _settings.SelectedFolder = value;
            SaveSettings();
        }
    }


    private UserSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<UserSettings>(json);
                return settings ?? new UserSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading user settings: {ex.Message}");
        }
        return new UserSettings();
    }

    private void SaveSettings()
    {
        try
        {
            if (string.IsNullOrEmpty(_settingsFilePath))
                return;
                
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving user settings: {ex.Message}");
        }
    }

    private class UserSettings
    {
        public string? SelectedFolder { get; set; }
    }
}

