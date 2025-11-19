using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public string Nickname
    {
        get => _settings.Nickname ?? string.Empty;
        set
        {
            _settings.Nickname = value;
            SaveSettings();
        }
    }

    public bool EnableTraditionalChinese
    {
        get => _settings.EnableTraditionalChinese ?? false;
        set
        {
            _settings.EnableTraditionalChinese = value;
            SaveSettings();
        }
    }

    public string UILanguage
    {
        get => _settings.UILanguage ?? "en-US";
        set
        {
            _settings.UILanguage = value;
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

    public List<string> RecentFolders
    {
        get => _settings.RecentFolders ?? new List<string>();
        set
        {
            _settings.RecentFolders = value;
            SaveSettings();
        }
    }

    public void AddRecentFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        var recentFolders = RecentFolders.ToList();
        
        // Remove if already exists (to avoid duplicates)
        recentFolders.Remove(folderPath);
        
        // Add to the beginning
        recentFolders.Insert(0, folderPath);
        
        // Keep only the last 10
        if (recentFolders.Count > 10)
        {
            recentFolders = recentFolders.Take(10).ToList();
        }
        
        RecentFolders = recentFolders;
    }

    private class UserSettings
    {
        public string? SelectedFolder { get; set; }
        public string? Nickname { get; set; }
        public bool? EnableTraditionalChinese { get; set; }
        public string? UILanguage { get; set; }
        public List<string>? RecentFolders { get; set; }
    }
}

