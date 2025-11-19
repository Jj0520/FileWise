using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileWise.Utilities;
using FileWise.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FileWise.Views;

public partial class SettingsWindow : Window
{
    private readonly StringBuilder _logBuffer = new();
    private readonly object _lockObject = new();
    private const int MaxLogLength = 50000; // Keep last ~50k characters
    private readonly IConfiguration _configuration;
    private readonly string _configFilePath;
    private bool _isLoadingSettings;
    private UserSettingsService? _userSettingsService;
    private bool _apiSettingsRequireRestart = false;

    public SettingsWindow(IConfiguration configuration)
    {
        _configuration = configuration;
        
        // Get UserSettingsService from App's service provider
        try
        {
            _userSettingsService = ((App)System.Windows.Application.Current).GetService<UserSettingsService>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting UserSettingsService: {ex.Message}");
        }
        
        // Get config file path
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var exeDirectory = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(exeDirectory))
        {
            exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }
        _configFilePath = Path.Combine(exeDirectory, "appsettings.json");
        
        InitializeComponent();
        
        // Subscribe to language changes
        LocalizationService.Instance.LanguageChanged += LocalizationService_LanguageChanged;
        
        // Load current settings
        LoadSettings();
        
        // Update UI strings based on current language
        UpdateUIStrings();
        
        // Redirect console output (but don't show by default)
        var consoleWriter = new ConsoleLogWriter(this);
        Console.SetOut(consoleWriter);
        
        // Redirect trace output
        System.Diagnostics.Trace.Listeners.Add(new TraceLogListener(this));
        
        Loaded += SettingsWindow_Loaded;
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Scroll to bottom on load if console is visible
        if (ConsoleLogBorder.Visibility == Visibility.Visible)
        {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ConsoleScrollViewer.ScrollToEnd();
        }), DispatcherPriority.Loaded);
        }
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
    {
        // Update UI strings when language changes
        Dispatcher.Invoke(() => UpdateUIStrings());
    }

    private void UpdateUIStrings()
    {
        try
        {
            var loc = LocalizationService.Instance;
            
            // Update window title
            if (SettingsTitle != null)
                SettingsTitle.Text = loc.GetString("Settings_Title");
            Title = loc.GetString("Window_Settings");
            
            // Update console log button
            if (ConsoleLogToggleButton != null)
            {
                ConsoleLogToggleButton.Content = ConsoleLogBorder.Visibility == Visibility.Visible 
                    ? loc.GetString("Button_HideConsoleLog") 
                    : loc.GetString("Button_ShowConsoleLog");
            }
            
            // Update API Configuration section
            if (APIConfigurationTitle != null)
                APIConfigurationTitle.Text = loc.GetString("Settings_API_Configuration");
            if (APIModeLabel != null)
                APIModeLabel.Text = loc.GetString("Settings_API_Mode");
            if (ApiKeyModeRadio != null)
                ApiKeyModeRadio.Content = loc.GetString("Settings_API_Mode_ApiKey");
            if (LocalhostModeRadio != null)
                LocalhostModeRadio.Content = loc.GetString("Settings_API_Mode_Localhost");
            if (APIKeyLabel != null)
                APIKeyLabel.Text = loc.GetString("Settings_API_Key");
            if (APIKeyDescription != null)
                APIKeyDescription.Text = loc.GetString("Settings_API_Key_Description");
            if (LocalhostURLLabel != null)
                LocalhostURLLabel.Text = loc.GetString("Settings_Localhost_URL");
            if (LocalhostURLDescription != null)
                LocalhostURLDescription.Text = loc.GetString("Settings_Localhost_URL_Description");
            if (SaveAPISettingsButton != null)
                SaveAPISettingsButton.Content = loc.GetString("Button_SaveAPISettings");
            
            // Update User Profile section
            if (UserProfileTitle != null)
                UserProfileTitle.Text = loc.GetString("Settings_User_Profile");
            if (NicknameLabel != null)
                NicknameLabel.Text = loc.GetString("Settings_Nickname");
            if (NicknameDescription != null)
                NicknameDescription.Text = loc.GetString("Settings_Nickname_Description");
            if (SaveProfileSettingsButton != null)
                SaveProfileSettingsButton.Content = loc.GetString("Button_SaveProfileSettings");
            
            // Update Appearance section
            if (AppearanceTitle != null)
                AppearanceTitle.Text = loc.GetString("Settings_Appearance");
            if (AppearanceDescription != null)
                AppearanceDescription.Text = loc.GetString("Settings_Appearance_Description");
            if (LightThemeRadio != null)
                LightThemeRadio.Content = loc.GetString("Settings_Theme_Light");
            if (DarkThemeRadio != null)
                DarkThemeRadio.Content = loc.GetString("Settings_Theme_Dark");
            if (SystemThemeRadio != null)
                SystemThemeRadio.Content = loc.GetString("Settings_Theme_System");
            if (ThemeTip != null)
                ThemeTip.Text = loc.GetString("Settings_Theme_Tip");
            
            // Update UI Language section
            if (UILanguageTitle != null)
                UILanguageTitle.Text = loc.GetString("Settings_UI_Language");
            if (UILanguageLabel != null)
                UILanguageLabel.Text = loc.GetString("Settings_UI_Language");
            if (UILanguageDescription != null)
                UILanguageDescription.Text = loc.GetString("Settings_UI_Language_Description");
            if (SaveUISettingsButton != null)
                SaveUISettingsButton.Content = loc.GetString("Button_SaveUISettings");
            
            // Update OCR Language section
            if (OCRLanguageTitle != null)
                OCRLanguageTitle.Text = loc.GetString("Settings_OCR_Language");
            if (TraditionalChineseCheckBox != null)
                TraditionalChineseCheckBox.Content = loc.GetString("Settings_OCR_TraditionalChinese");
            if (OCRDescription != null)
                OCRDescription.Text = loc.GetString("Settings_OCR_Description");
            if (SaveOCRSettingsButton != null)
                SaveOCRSettingsButton.Content = loc.GetString("Button_SaveOCRSettings");
            
            // Update Indexing section
            if (IndexingTitle != null)
                IndexingTitle.Text = loc.GetString("Settings_Indexing");
            if (ChunkSizeLabel != null)
                ChunkSizeLabel.Text = loc.GetString("Settings_Indexing_ChunkSize");
            if (ChunkSizeDescription != null)
                ChunkSizeDescription.Text = loc.GetString("Settings_Indexing_ChunkSize_Description");
            if (MaxConcurrentFilesLabel != null)
                MaxConcurrentFilesLabel.Text = loc.GetString("Settings_Indexing_MaxConcurrentFiles");
            if (MaxConcurrentFilesDescription != null)
                MaxConcurrentFilesDescription.Text = loc.GetString("Settings_Indexing_MaxConcurrentFiles_Description");
            if (MaxConcurrentPdfsLabel != null)
                MaxConcurrentPdfsLabel.Text = loc.GetString("Settings_Indexing_MaxConcurrentPdfs");
            if (MaxConcurrentPdfsDescription != null)
                MaxConcurrentPdfsDescription.Text = loc.GetString("Settings_Indexing_MaxConcurrentPdfs_Description");
            if (SaveIndexingSettingsButton != null)
                SaveIndexingSettingsButton.Content = loc.GetString("Button_SaveIndexingSettings");
            
            // Update ComboBox items
            if (UILanguageComboBox != null && UILanguageComboBox.Items.Count >= 2)
            {
                if (UILanguageComboBox.Items[0] is ComboBoxItem item1)
                    item1.Content = loc.GetString("Settings_UI_Language_English");
                if (UILanguageComboBox.Items[1] is ComboBoxItem item2)
                    item2.Content = loc.GetString("Settings_UI_Language_TraditionalChinese");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating UI strings: {ex.Message}");
        }
    }

    private void LoadSettings()
    {
        try
        {
            _isLoadingSettings = true;
            // Load API Mode
            var useLocalhost = _configuration["Gemini:UseLocalhost"]?.ToLower() == "true";
            if (useLocalhost)
            {
                LocalhostModeRadio.IsChecked = true;
                ApiKeySection.Visibility = Visibility.Collapsed;
                LocalhostSection.Visibility = Visibility.Visible;
            }
            else
            {
                ApiKeyModeRadio.IsChecked = true;
                ApiKeySection.Visibility = Visibility.Visible;
                LocalhostSection.Visibility = Visibility.Collapsed;
            }
            
            // Load API Key
            var apiKey = _configuration["Gemini:ApiKey"] ?? string.Empty;
            ApiKeyPasswordBox.Password = apiKey;
            
            // Load Localhost URL
            var localhostUrl = _configuration["Gemini:LocalhostUrl"] ?? "http://localhost:11434";
            LocalhostUrlTextBox.Text = localhostUrl;
            
            // Load Indexing settings
            ChunkSizeTextBox.Text = _configuration["Indexing:ChunkSize"] ?? "1000";
            MaxConcurrentFilesTextBox.Text = _configuration["Indexing:MaxConcurrentFiles"] ?? "5";
            MaxConcurrentPdfsTextBox.Text = _configuration["Indexing:MaxConcurrentPdfs"] ?? "2";

            // Load nickname and UI language
            if (_userSettingsService != null)
            {
                NicknameTextBox.Text = _userSettingsService.Nickname ?? string.Empty;
                TraditionalChineseCheckBox.IsChecked = _userSettingsService.EnableTraditionalChinese;
                
                // Load UI language
                var savedLanguage = _userSettingsService.UILanguage ?? "en-US";
                foreach (ComboBoxItem item in UILanguageComboBox.Items)
                {
                    if (item.Tag?.ToString() == savedLanguage)
                    {
                        UILanguageComboBox.SelectedItem = item;
                        break;
                    }
                }
                if (UILanguageComboBox.SelectedItem == null && UILanguageComboBox.Items.Count > 0)
                {
                    UILanguageComboBox.SelectedIndex = 0; // Default to first item
                }
            }

            var themePreference = ThemeManager.ParsePreference(_configuration["Appearance:Theme"]);
            switch (themePreference)
            {
                case ThemePreference.Light:
                    LightThemeRadio.IsChecked = true;
                    break;
                case ThemePreference.Dark:
                    DarkThemeRadio.IsChecked = true;
                    break;
                default:
                    SystemThemeRadio.IsChecked = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            System.Windows.MessageBox.Show($"Error loading settings: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }
    
    private void ApiModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (ApiKeyModeRadio.IsChecked == true)
        {
            ApiKeySection.Visibility = Visibility.Visible;
            LocalhostSection.Visibility = Visibility.Collapsed;
        }
        else if (LocalhostModeRadio.IsChecked == true)
        {
            ApiKeySection.Visibility = Visibility.Collapsed;
            LocalhostSection.Visibility = Visibility.Visible;
        }
    }

    private void SaveApiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Save API Mode
            var useLocalhost = LocalhostModeRadio.IsChecked == true;
            SaveConfigValue("Gemini:UseLocalhost", useLocalhost.ToString().ToLower());
            
            if (useLocalhost)
            {
                // Validate and save Localhost URL
                var localhostUrl = LocalhostUrlTextBox.Text.Trim();
                if (string.IsNullOrEmpty(localhostUrl))
                {
                    System.Windows.MessageBox.Show("Localhost URL cannot be empty.", "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                
                if (!Uri.TryCreate(localhostUrl, UriKind.Absolute, out _))
                {
                    System.Windows.MessageBox.Show("Please enter a valid URL (e.g., http://localhost:11434).", "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                
                SaveConfigValue("Gemini:LocalhostUrl", localhostUrl);
            }
            else
            {
                // Validate and save API Key
                var apiKey = ApiKeyPasswordBox.Password.Trim();
                if (string.IsNullOrEmpty(apiKey))
                {
                    System.Windows.MessageBox.Show("API Key cannot be empty when using API Key mode.", "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                
                SaveConfigValue("Gemini:ApiKey", apiKey);
            }
            
            _apiSettingsRequireRestart = true;
            
            var result = System.Windows.MessageBox.Show(
                "API settings saved successfully. The application needs to restart for changes to take effect.\n\nDo you want to restart now?",
                "Settings Saved - Restart Required", 
                System.Windows.MessageBoxButton.YesNo, 
                System.Windows.MessageBoxImage.Information);
            
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // Close the entire application
                System.Windows.Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error saving API settings: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void SaveUISettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_userSettingsService != null && UILanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                var languageCode = selectedItem.Tag?.ToString() ?? "en-US";
                _userSettingsService.UILanguage = languageCode;
                LocalizationService.Instance.SetLanguage(languageCode);
                
                // Update UI strings immediately
                UpdateUIStrings();
                
                System.Windows.MessageBox.Show(LocalizationService.Instance.GetString("Message_SettingsSaved"), 
                    LocalizationService.Instance.GetString("Window_Settings"), 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error saving UI settings: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void SaveOcrSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_userSettingsService != null)
            {
                _userSettingsService.EnableTraditionalChinese = TraditionalChineseCheckBox.IsChecked == true;
                System.Windows.MessageBox.Show("OCR settings saved successfully. The Traditional Chinese language support will be used for new indexing operations.", 
                    "Settings Saved", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error saving OCR settings: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void SaveIndexingSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Validate chunk size
            if (!int.TryParse(ChunkSizeTextBox.Text, out int chunkSize) || chunkSize <= 0)
            {
                System.Windows.MessageBox.Show("Chunk Size must be a positive integer.", "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Validate max concurrent files
            if (!int.TryParse(MaxConcurrentFilesTextBox.Text, out int maxFiles) || maxFiles <= 0)
            {
                System.Windows.MessageBox.Show("Max Concurrent Files must be a positive integer.", "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Validate max concurrent PDFs
            if (!int.TryParse(MaxConcurrentPdfsTextBox.Text, out int maxPdfs) || maxPdfs <= 0)
            {
                System.Windows.MessageBox.Show("Max Concurrent PDFs must be a positive integer.", "Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            SaveConfigValue("Indexing:ChunkSize", chunkSize.ToString());
            SaveConfigValue("Indexing:MaxConcurrentFiles", maxFiles.ToString());
            SaveConfigValue("Indexing:MaxConcurrentPdfs", maxPdfs.ToString());

            _apiSettingsRequireRestart = true;
            
            var result = System.Windows.MessageBox.Show(
                "Indexing settings saved successfully. The application needs to restart for changes to take effect.\n\nDo you want to restart now?",
                "Settings Saved - Restart Required", 
                System.Windows.MessageBoxButton.YesNo, 
                System.Windows.MessageBoxImage.Information);
            
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // Close the entire application
                System.Windows.Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error saving indexing settings: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void SaveConfigValue(string key, string value)
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                // Create default config file
                var defaultConfig = new
                {
                    Database = new { ConnectionString = "Data Source=filewise.db" },
                    Indexing = new { ChunkSize = 1000, MaxConcurrentFiles = 5, MaxConcurrentPdfs = 2 },
                    Gemini = new { ApiKey = "" },
                    Appearance = new { Theme = "System" }
                };
                var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }

            // Read existing config
            var jsonContent = File.ReadAllText(_configFilePath);
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // Create a dictionary to modify
            var dict = new Dictionary<string, object?>();
            
            // Copy existing values
            if (root.TryGetProperty("Database", out var db))
                dict["Database"] = JsonSerializer.Deserialize<object>(db.GetRawText());
            if (root.TryGetProperty("Indexing", out var idx))
                dict["Indexing"] = JsonSerializer.Deserialize<object>(idx.GetRawText());
            if (root.TryGetProperty("Gemini", out var gem))
                dict["Gemini"] = JsonSerializer.Deserialize<object>(gem.GetRawText());
            if (root.TryGetProperty("Appearance", out var appearance))
                dict["Appearance"] = JsonSerializer.Deserialize<object>(appearance.GetRawText());

            // Update the specific value
            var keyParts = key.Split(':');
            if (keyParts.Length == 2)
            {
                var section = keyParts[0];
                var property = keyParts[1];

                Dictionary<string, object?> sectionDict;

                if (!dict.ContainsKey(section))
                {
                    sectionDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                }
                else if (dict[section] is JsonElement sectionElement)
                {
                    sectionDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(sectionElement.GetRawText())
                                   ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                }
                else if (dict[section] is Dictionary<string, object?> existingDict)
                {
                    sectionDict = existingDict;
                }
                else
                {
                    sectionDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                }

                sectionDict[property] = value;
                dict[section] = sectionDict;
            }

            // Write back to file
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(dict, options);
            File.WriteAllText(_configFilePath, updatedJson);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving config value: {ex.Message}");
            throw;
        }
    }

    private void ConsoleLogToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConsoleLogBorder.Visibility == Visibility.Visible)
        {
            ConsoleLogBorder.Visibility = Visibility.Collapsed;
            ConsoleLogRow.Height = new GridLength(0);
            ConsoleLogToggleButton.Content = LocalizationService.Instance.GetString("Button_ShowConsoleLog");
        }
        else
        {
            ConsoleLogBorder.Visibility = Visibility.Visible;
            ConsoleLogRow.Height = new GridLength(300);
            ConsoleLogToggleButton.Content = LocalizationService.Instance.GetString("Button_HideConsoleLog");
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ConsoleScrollViewer.ScrollToEnd();
            }), DispatcherPriority.Loaded);
        }
    }

    public void AppendLog(string message)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            lock (_lockObject)
            {
                _logBuffer.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                
                // Trim if too long
                if (_logBuffer.Length > MaxLogLength)
                {
                    var excess = _logBuffer.Length - MaxLogLength;
                    var index = _logBuffer.ToString().IndexOf('\n', excess) + 1;
                    if (index > 0)
                    {
                        _logBuffer.Remove(0, index);
                    }
                }
                
                ConsoleTextBox.Text = _logBuffer.ToString();
                if (ConsoleLogBorder.Visibility == Visibility.Visible)
                {
                ConsoleScrollViewer.ScrollToEnd();
                }
            }
        }), DispatcherPriority.Normal);
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        lock (_lockObject)
        {
            _logBuffer.Clear();
            ConsoleTextBox.Text = string.Empty;
        }
    }

    private ThemePreference GetSelectedThemePreference()
    {
        if (LightThemeRadio.IsChecked == true)
        {
            return ThemePreference.Light;
        }

        if (DarkThemeRadio.IsChecked == true)
        {
            return ThemePreference.Dark;
        }

        return ThemePreference.System;
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        var preference = GetSelectedThemePreference();
        ThemeManager.ApplyTheme(preference);
        try
        {
            SaveConfigValue("Appearance:Theme", preference.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving theme preference: {ex.Message}");
        }
    }

    private void SaveProfileSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_userSettingsService != null)
            {
                _userSettingsService.Nickname = NicknameTextBox.Text.Trim();
                System.Windows.MessageBox.Show("Profile settings saved successfully.", 
                    "Settings Saved", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("User settings service is not available.", 
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error saving profile settings: {ex.Message}", 
                "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // If API settings require restart, close the entire application
        if (_apiSettingsRequireRestart)
        {
            System.Windows.Application.Current.Shutdown();
        }
        else
        {
            Close();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // If API settings require restart, close the entire application
        if (_apiSettingsRequireRestart)
        {
            e.Cancel = true; // Cancel the window close
            System.Windows.Application.Current.Shutdown(); // Close entire application instead
        }
        else
        {
            base.OnClosing(e);
        }
    }

    private class ConsoleLogWriter : TextWriter
    {
        private readonly SettingsWindow _window;

        public ConsoleLogWriter(SettingsWindow window)
        {
            _window = window;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            if (value != null)
            {
                _window.AppendLog(value);
            }
        }

        public override void Write(string? value)
        {
            if (value != null)
            {
                _window.AppendLog(value);
            }
        }
    }

    private class TraceLogListener : System.Diagnostics.TraceListener
    {
        private readonly SettingsWindow _window;

        public TraceLogListener(SettingsWindow window)
        {
            _window = window;
        }

        public override void Write(string? message)
        {
            if (message != null)
            {
                _window.AppendLog($"[DEBUG] {message}");
            }
        }

        public override void WriteLine(string? message)
        {
            if (message != null)
            {
                _window.AppendLog($"[DEBUG] {message}");
            }
        }
    }
}
