    using System.Collections.Generic;
using System.IO;
using System.Windows;
using FileWise.Services;
using Application = System.Windows.Application;
using FileWise.Utilities;
using FileWise.ViewModels;
using FileWise.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FileWise;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Handle unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // Build configuration - use executable directory (works globally regardless of installation location)
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var exeDirectory = Path.GetDirectoryName(exePath);
        
        // Fallback to BaseDirectory if Location is null
        if (string.IsNullOrEmpty(exeDirectory))
        {
            exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }
        
        // Normalize path to handle any path issues
        exeDirectory = Path.GetFullPath(exeDirectory);
        var configPath = Path.Combine(exeDirectory, "appsettings.json");
        
        // Log for debugging
        System.Diagnostics.Debug.WriteLine($"=== Configuration Loading ===");
        System.Diagnostics.Debug.WriteLine($"Executable path: {exePath}");
        System.Diagnostics.Debug.WriteLine($"Executable directory: {exeDirectory}");
        System.Diagnostics.Debug.WriteLine($"Config path: {configPath}");
        System.Diagnostics.Debug.WriteLine($"Config exists: {File.Exists(configPath)}");
        
        // Helper function to create default configuration
        IConfiguration CreateDefaultConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Database:ConnectionString", "Data Source=filewise.db" },
                    { "Indexing:ChunkSize", "1000" },
                    { "Indexing:MaxConcurrentFiles", "50" },
                    { "Indexing:MaxConcurrentPdfs", "20" },
                    { "Gemini:ApiKey", "" },
                    { "Appearance:Theme", "System" }
                })
                .Build();
        }
        
        // Initialize with defaults, then try to load from file
        IConfiguration configuration = CreateDefaultConfiguration();
        
        // Load from executable directory (works globally)
        if (File.Exists(configPath) && !string.IsNullOrEmpty(exeDirectory))
        {
            try
            {
                configuration = new ConfigurationBuilder()
                    .SetBasePath(exeDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();
                System.Diagnostics.Debug.WriteLine($"✓ Configuration loaded from: {configPath}");
                
                // Verify API key was loaded (show first/last 4 chars for security)
                var loadedApiKey = configuration["Gemini:ApiKey"];
                if (!string.IsNullOrEmpty(loadedApiKey))
                {
                    var maskedKey = loadedApiKey.Length > 8 
                        ? $"{loadedApiKey.Substring(0, 4)}...{loadedApiKey.Substring(loadedApiKey.Length - 4)}" 
                        : "***";
                    System.Diagnostics.Debug.WriteLine($"✓ API Key loaded: {maskedKey}");
                    Console.WriteLine($"✓ Configuration loaded from: {configPath}");
                    Console.WriteLine($"✓ API Key loaded: {maskedKey}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠ API Key not found in configuration!");
                    Console.WriteLine($"⚠ API Key not found in configuration!");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ Error loading config file: {ex.Message}");
                // Keep default configuration
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"⚠ Config file not found at: {configPath}. Using defaults.");
        }

        // Apply theme before UI initializes
        var themePreferenceValue = configuration["Appearance:Theme"];
        var themePreference = ThemeManager.ParsePreference(themePreferenceValue);
        ThemeManager.ApplyTheme(themePreference);

        // Setup dependency injection
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection, configuration);
        _serviceProvider = serviceCollection.BuildServiceProvider();

        // Initialize database
        var dbService = _serviceProvider.GetRequiredService<IDatabaseService>();
        dbService.InitializeAsync().Wait();

        // Create and show main window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        System.Diagnostics.Debug.WriteLine($"Unhandled Exception: {exception?.Message}");
        System.Diagnostics.Debug.WriteLine($"Stack trace: {exception?.StackTrace}");
        
        // Don't show error dialog for known issues like missing/incompatible pdfium.dll - these are handled gracefully
        if (exception != null && 
            (exception is System.DllNotFoundException || 
             exception is System.BadImageFormatException ||
             exception is System.EntryPointNotFoundException ||
             exception.Message.Contains("pdfium.dll") || 
             exception.Message.Contains("Unable to load DLL") ||
             exception.Message.Contains("Unable to find an entry point")))
        {
            System.Diagnostics.Debug.WriteLine($"Suppressing error dialog for known issue: {exception.Message}");
            return;
        }
        
        System.Windows.MessageBox.Show(
            $"An unexpected error occurred:\n\n{exception?.Message}\n\nThe application will continue, but some features may not work correctly.",
            "Unexpected Error",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Dispatcher Unhandled Exception: {e.Exception.Message}");
        System.Diagnostics.Debug.WriteLine($"Stack trace: {e.Exception.StackTrace}");
        
        // Don't show error dialog for known issues - these are handled gracefully
        if (e.Exception is System.DllNotFoundException || 
            e.Exception is System.BadImageFormatException ||
            e.Exception is System.EntryPointNotFoundException ||
            e.Exception.Message.Contains("pdfium.dll") || 
            e.Exception.Message.Contains("Unable to load DLL") ||
            e.Exception.Message.Contains("Unable to find an entry point") ||
            e.Exception.Message.Contains("appsettings.json") ||
            e.Exception.Message.Contains("configuration file"))
        {
            System.Diagnostics.Debug.WriteLine($"Suppressing error dialog for known issue: {e.Exception.Message}");
            e.Handled = true; // Prevent app crash
            return;
        }
        
        System.Windows.MessageBox.Show(
            $"An error occurred in the UI:\n\n{e.Exception.Message}\n\nThe application will try to continue.",
            "UI Error",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
        
        e.Handled = true; // Prevent app crash
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Unobserved Task Exception: {e.Exception.Message}");
        System.Diagnostics.Debug.WriteLine($"Stack trace: {e.Exception.StackTrace}");
        
        // Check if any exception in the aggregate is related to pdfium.dll
        var innerException = e.Exception.InnerException ?? e.Exception;
        var exceptionMessage = innerException.Message;
        
        // Don't show error dialog for known issues like missing pdfium.dll - these are handled gracefully
        if (innerException is System.DllNotFoundException || 
            innerException is System.BadImageFormatException ||
            exceptionMessage.Contains("pdfium.dll") || 
            exceptionMessage.Contains("Unable to load DLL"))
        {
            System.Diagnostics.Debug.WriteLine($"Suppressing error dialog for known issue: {exceptionMessage}");
            e.SetObserved(); // Mark as handled
            return;
        }
        
        System.Windows.MessageBox.Show(
            $"An error occurred in a background task:\n\n{exceptionMessage}\n\nThe application will continue.",
            "Background Task Error",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
        
        e.SetObserved(); // Mark as handled
    }

    private void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Configuration
        services.AddSingleton(configuration);

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Database
        var connectionString = configuration["Database:ConnectionString"] ?? "Data Source=filewise.db";
        services.AddSingleton<IDatabaseService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DatabaseService>>();
            return new DatabaseService(connectionString, logger);
        });

        // Services
        services.AddSingleton<UserSettingsService>();
        services.AddSingleton<ChatHistoryService>();
        services.AddSingleton<TextExtractorService>(sp => 
            new TextExtractorService(configuration));
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddSingleton<IVectorSearchService, VectorSearchService>();
        services.AddSingleton<IFileIndexerService, FileIndexerService>();
        services.AddSingleton<IChatbotService, ChatbotService>();
        
        // Note: TextExtractorService is already registered above, so it will be injected into MainViewModel

        // ViewModels
        services.AddTransient<MainViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }

    public T? GetService<T>() where T : class
    {
        return _serviceProvider?.GetService<T>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        ThemeManager.Dispose();
        base.OnExit(e);
    }

}

