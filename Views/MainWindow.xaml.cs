using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using FileWise.Models;
using FileWise.ViewModels;
using FileWise.Utilities;
using FileWise.Views;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FileWise.Views;

public partial class MainWindow : Window
{
    private HwndSource? _hwndSource;
    private static SettingsWindow? _settingsWindowInstance;
    
    // Windows API constants and methods for native snap and resize
    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_NCHITTEST = 0x0084;
    private const int SC_MOVE = 0xF010;
    private const int HT_CAPTION = 0x2;
    
    // Hit test values for resize
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    
    // Resize border thickness
    private const int RESIZE_BORDER_THICKNESS = 6;
    
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);
    
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38; // Windows 11 snap layout support
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33; // Windows 11 rounded corners
    
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    public MainWindow(MainViewModel viewModel)
    {
        try
        {
            InitializeComponent();
            DataContext = viewModel;
            
            // Ensure window is positioned correctly on startup
            Loaded += MainWindow_Loaded;
            ContentRendered += MainWindow_ContentRendered;
            StateChanged += MainWindow_StateChanged;
            
            // Enable native Windows snap functionality
            SourceInitialized += MainWindow_SourceInitialized;
            
            // Scroll chat to bottom when new messages are added
            if (viewModel != null)
            {
                viewModel.ChatMessages.CollectionChanged += (s, e) =>
                {
                    try
                    {
                        if (ChatScrollViewer != null)
                        {
                            ChatScrollViewer.ScrollToEnd();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error scrolling chat: {ex.Message}");
                    }
                };
                
                // Update DataGrid layout when chat panel toggles
                viewModel.PropertyChanged += (s, e) =>
                {
                    try
                    {
                        if (e.PropertyName == nameof(MainViewModel.IsChatPanelOpen))
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                // Handle column widths based on IsChatPanelOpen to override GridSplitter modifications
                                if (ContentColumnsGrid != null && ContentColumnsGrid.ColumnDefinitions.Count >= 4)
                                {
                                    var converter = new BooleanToColumnWidthConverter();
                                    
                                    if (!viewModel.IsChatPanelOpen)
                                    {
                                        // Close: Clear bindings and set both columns to 0
                                        BindingOperations.ClearBinding(ContentColumnsGrid.ColumnDefinitions[2], ColumnDefinition.WidthProperty);
                                        ContentColumnsGrid.ColumnDefinitions[2].Width = new GridLength(0);
                                        
                                        BindingOperations.ClearBinding(ContentColumnsGrid.ColumnDefinitions[3], ColumnDefinition.WidthProperty);
                                        ContentColumnsGrid.ColumnDefinitions[3].Width = new GridLength(0);
                                    }
                                    else
                                    {
                                        // Open: Restore bindings
                                        var binding2 = new System.Windows.Data.Binding(nameof(MainViewModel.IsChatPanelOpen))
                                        {
                                            Converter = converter,
                                            ConverterParameter = 4,
                                            Source = viewModel
                                        };
                                        BindingOperations.SetBinding(ContentColumnsGrid.ColumnDefinitions[2], ColumnDefinition.WidthProperty, binding2);
                                        
                                        var binding3 = new System.Windows.Data.Binding(nameof(MainViewModel.IsChatPanelOpen))
                                        {
                                            Converter = converter,
                                            ConverterParameter = 450,
                                            Source = viewModel
                                        };
                                        BindingOperations.SetBinding(ContentColumnsGrid.ColumnDefinitions[3], ColumnDefinition.WidthProperty, binding3);
                                    }
                                    
                                    // Force layout invalidation to ensure columns recalculate
                                    ContentColumnsGrid.InvalidateMeasure();
                                    ContentColumnsGrid.InvalidateArrange();
                                }
                                UpdateLayout();
                            }), System.Windows.Threading.DispatcherPriority.Loaded);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error updating layout: {ex.Message}");
                    }
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing MainWindow: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            // Show error to user
            System.Windows.MessageBox.Show($"Error initializing application: {ex.Message}", "Error", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            // Get window handle for Windows API calls
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (_hwndSource != null)
            {
                _hwndSource.AddHook(WndProc);
                
                // Enable Windows 11 snap layout support
                try
                {
                    var backdropType = 2; // DWMSBT_MAINWINDOW
                    DwmSetWindowAttribute(_hwndSource.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
                }
                catch
                {
                    // Ignore if Windows 11 specific API is not available
                }
                
                // Enable Windows 11 native rounded corners (DWMWCP_ROUND = 2)
                try
                {
                    var cornerPreference = 2; // DWMWCP_ROUND - Use native Windows 11 rounded corners
                    DwmSetWindowAttribute(_hwndSource.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
                }
                catch
                {
                    // Ignore if Windows 11 specific API is not available
                }
                
                // Apply rounded corner clip to the window itself to prevent sharp corners showing through
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateWindowCornerRadius();
                    UpdateWindowClip();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing Windows API: {ex.Message}");
        }
    }
    
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Handle Windows messages for native snap and resize behavior
        
        // WM_NCHITTEST - Handle hit testing for resize (only when on resize border)
        if (msg == WM_NCHITTEST && WindowState != WindowState.Maximized)
        {
            // Get cursor position in screen coordinates
            var screenPoint = new Point(
                (int)lParam & 0xFFFF,
                ((int)lParam >> 16) & 0xFFFF
            );
            
            // Convert to window coordinates
            var windowPoint = PointFromScreen(screenPoint);
            
            // Check if we're on a resize border
            var hitTestResult = HitTestResize(windowPoint);
            if (hitTestResult != 0)
            {
                // ONLY mark as handled if we're actually on a resize border
                handled = true;
                return new IntPtr(hitTestResult);
            }
            
            // If not on a resize border, DON'T handle it - let it pass through normally
            // This allows clicks, dragging, etc. to work
        }
        
        // WM_WINDOWPOSCHANGED (0x0047) - Window position/size changed (including after snap)
        const int WM_WINDOWPOSCHANGED = 0x0047;
        const int WM_EXITSIZEMOVE = 0x0232; // Window finished moving/resizing
        
        if (msg == WM_WINDOWPOSCHANGED || msg == WM_EXITSIZEMOVE)
        {
            // Ensure rounded corners are maintained after Windows snap operations
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    UpdateWindowCornerRadius();
                    UpdateWindowClip();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating window corners after snap: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        
        return IntPtr.Zero;
    }
    
    private int HitTestResize(Point point)
    {
        // Don't allow resize when maximized
        if (WindowState == WindowState.Maximized)
            return 0;
        
        var width = ActualWidth;
        var height = ActualHeight;
        
        // Check corners first (they have priority)
        // Top-left corner
        if (point.X <= RESIZE_BORDER_THICKNESS && point.Y <= RESIZE_BORDER_THICKNESS)
            return HTTOPLEFT;
        
        // Top-right corner
        if (point.X >= width - RESIZE_BORDER_THICKNESS && point.Y <= RESIZE_BORDER_THICKNESS)
            return HTTOPRIGHT;
        
        // Bottom-left corner
        if (point.X <= RESIZE_BORDER_THICKNESS && point.Y >= height - RESIZE_BORDER_THICKNESS)
            return HTBOTTOMLEFT;
        
        // Bottom-right corner
        if (point.X >= width - RESIZE_BORDER_THICKNESS && point.Y >= height - RESIZE_BORDER_THICKNESS)
            return HTBOTTOMRIGHT;
        
        // Check edges
        // Left edge
        if (point.X <= RESIZE_BORDER_THICKNESS)
            return HTLEFT;
        
        // Right edge
        if (point.X >= width - RESIZE_BORDER_THICKNESS)
            return HTRIGHT;
        
        // Top edge
        if (point.Y <= RESIZE_BORDER_THICKNESS)
            return HTTOP;
        
        // Bottom edge
        if (point.Y >= height - RESIZE_BORDER_THICKNESS)
            return HTBOTTOM;
        
        // Not on a resize border - return 0 so normal mouse handling continues
        return 0;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Set window height to 80% of the monitor height (20% reduction)
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var targetHeight = screenHeight * 0.8;
            
            // Only set height on initial load (check if it's still at default or close to it)
            if (Math.Abs(ActualHeight - 900) < 10 || ActualHeight == 0 || Math.Abs(Height - 900) < 10)
            {
                Height = targetHeight;
            }
            
            PositionWindowCorrectly();
            UpdateMaximizeButton();
            UpdateWindowCornerRadius();
            
            // Update clip when window size changes
            SizeChanged += (s, args) => 
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateWindowCornerRadius();
                        UpdateWindowClip();
                    }), System.Windows.Threading.DispatcherPriority.Render);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in SizeChanged handler: {ex.Message}");
                }
            };
            
            // Update corners when window position changes (after snap)
            LocationChanged += (s, args) =>
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateWindowCornerRadius();
                        UpdateWindowClip();
                    }), System.Windows.Threading.DispatcherPriority.Render);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in LocationChanged handler: {ex.Message}");
                }
            };
            
            // Initial clip update after layout - use Render priority to ensure it happens after rendering
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateWindowCornerRadius();
                UpdateWindowClip();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in MainWindow_Loaded: {ex.Message}");
        }
    }

    private void MainWindow_ContentRendered(object sender, EventArgs e)
    {
        PositionWindowCorrectly();
        // Update corners and clip after content is rendered
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateWindowCornerRadius();
            UpdateWindowClip();
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void PositionWindowCorrectly()
    {
        // Get the working area (excludes taskbar) to ensure proper positioning
        var workingArea = SystemParameters.WorkArea;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
        var windowHeight = ActualHeight > 0 ? ActualHeight : Height;
        
        // Calculate centered horizontal position using working area
        var leftPosition = workingArea.Left + Math.Max(0, (workingArea.Width - windowWidth) / 2);
        
        // Ensure window is positioned at least 50 pixels from top of working area
        // This ensures the title bar with buttons is always visible
        var topPosition = workingArea.Top + 50;
        
        // If centering vertically would work better, use that but ensure minimum 50px from top
        var verticalCenter = workingArea.Top + (workingArea.Height - windowHeight) / 2;
        if (verticalCenter >= workingArea.Top + 50)
        {
            topPosition = verticalCenter;
        }
        
        // If window extends beyond bottom of working area, adjust it
        if (topPosition + windowHeight > workingArea.Bottom)
        {
            topPosition = Math.Max(workingArea.Top + 50, workingArea.Bottom - windowHeight - 50);
        }
        
        // Ensure we're not positioning off-screen
        if (topPosition < 0)
        {
            topPosition = 50;
        }
        if (leftPosition < 0)
        {
            leftPosition = Math.Max(0, (screenWidth - windowWidth) / 2);
        }
        
        // Apply the positions directly
        Left = leftPosition;
        Top = topPosition;
    }


    private void CloseChatPanel_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsChatPanelOpen = false;
            // Force layout update to ensure DataGrid columns resize properly
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Clear bindings and set widths directly to override GridSplitter modifications
                if (ContentColumnsGrid != null && ContentColumnsGrid.ColumnDefinitions.Count >= 4)
                {
                    var converter = new BooleanToColumnWidthConverter();
                    
                    // Column 2: GridSplitter column - clear binding and set to 0
                    BindingOperations.ClearBinding(ContentColumnsGrid.ColumnDefinitions[2], ColumnDefinition.WidthProperty);
                    ContentColumnsGrid.ColumnDefinitions[2].Width = new GridLength(0);
                    
                    // Column 3: Chat panel column - clear binding and set to 0
                    BindingOperations.ClearBinding(ContentColumnsGrid.ColumnDefinitions[3], ColumnDefinition.WidthProperty);
                    ContentColumnsGrid.ColumnDefinitions[3].Width = new GridLength(0);
                    
                    // Force layout invalidation to ensure columns recalculate
                    ContentColumnsGrid.InvalidateMeasure();
                    ContentColumnsGrid.InvalidateArrange();
                }
                UpdateLayout();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void TextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            try
            {
                if (sender is System.Windows.Controls.TextBox textBox && DataContext is MainViewModel vm)
                {
                    // Update the binding source to ensure UserQuery is updated
                    var bindingExpression = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
                    bindingExpression?.UpdateSource();
                    
                    // Execute the command
                    if (vm.SendQueryCommand != null && vm.SendQueryCommand.CanExecute(null))
                    {
                        vm.SendQueryCommand.Execute(null);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error executing SendQueryCommand: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            e.Handled = true;
        }
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(vm.SearchQuery))
            {
                // Trigger search - you can add search functionality here
                // For now, just focus on the main content area
                e.Handled = true;
            }
        }
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (sender is DataGrid dg && dg.SelectedItem is FileMetadata file)
            {
                vm.OpenFileLocationFromListCommand.Execute(file);
            }
            else if (vm.SelectedSearchResult != null)
            {
                vm.OpenFileLocationCommand.Execute(vm.SelectedSearchResult);
            }
        }
    }

    private void FileSelectionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.Tag is FileMetadata file && DataContext is MainViewModel vm)
            {
                // With TwoWay binding, the property is already updated by the binding
                // We just need to sync the SelectedFilesForContext collection
                if (file.IsSelectedForContext)
                {
                    if (!vm.SelectedFilesForContext.Contains(file))
                    {
                        vm.SelectedFilesForContext.Add(file);
                    }
                }
                else
                {
                    vm.SelectedFilesForContext.Remove(file);
                }
                
                System.Diagnostics.Debug.WriteLine($"File {file.FileName} selection: {file.IsSelectedForContext} (collection count: {vm.SelectedFilesForContext.Count})");
                Console.WriteLine($"✓ File {file.FileName} selection: {file.IsSelectedForContext} (collection count: {vm.SelectedFilesForContext.Count})");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in FileSelectionCheckBox_Click: {ex.Message}");
            Console.WriteLine($"Error in FileSelectionCheckBox_Click: {ex.Message}");
        }
    }

    private void FileReindexCheckBox_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.Tag is FileMetadata file && DataContext is MainViewModel vm)
            {
                SyncReindexSelection(vm, file);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in FileReindexCheckBox_Click: {ex.Message}");
            Console.WriteLine($"Error in FileReindexCheckBox_Click: {ex.Message}");
        }
    }

    private void FileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is FileMetadata file)
            {
                System.Diagnostics.Debug.WriteLine($"FileMenuButton clicked for file: {file.FileName}, Type: {file.FileType}");
                Console.WriteLine($"FileMenuButton clicked for file: {file.FileName}, Type: {file.FileType}");
                
                var contextMenu = new ContextMenu();
                
                var reindexMenuItem = new MenuItem
                {
                    Header = "Re-index",
                    Icon = new TextBlock { Text = "🔄", FontSize = 14 }
                };
                reindexMenuItem.Click += (s, args) =>
                {
                    if (DataContext is MainViewModel viewModel)
                    {
                        System.Diagnostics.Debug.WriteLine($"Re-indexing file: {file.FileName}");
                        Console.WriteLine($"Re-indexing file: {file.FileName}");
                        viewModel.ReindexSingleFileCommand.Execute(file);
                    }
                };
                
                contextMenu.Items.Add(reindexMenuItem);
                contextMenu.IsOpen = true;
                contextMenu.PlacementTarget = button;
                contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"FileMenuButton_Click: sender is not Button or Tag is not FileMetadata. Sender type: {sender?.GetType()}");
                Console.WriteLine($"FileMenuButton_Click: sender is not Button or Tag is not FileMetadata");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in FileMenuButton_Click: {ex.Message}");
            Console.WriteLine($"Error in FileMenuButton_Click: {ex.Message}");
            System.Windows.MessageBox.Show($"Error opening file menu: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void FileItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is FrameworkElement element && element.DataContext is FileMetadata file)
        {
            if (vm.IsSelectionMode)
            {
                file.IsSelectedForReindex = !file.IsSelectedForReindex;
                SyncReindexSelection(vm, file);
                e.Handled = true;
                return;
            }

            // Check for double click
            if (e.ClickCount == 2)
            {
                vm.OpenFileLocationFromListCommand.Execute(file);
            }
            else
            {
                // Set selected file on single click
                vm.SelectedFile = file;
            }
        }
    }

    private static void SyncReindexSelection(MainViewModel vm, FileMetadata file)
    {
        if (file.IsSelectedForReindex)
        {
            if (!vm.SelectedFilesForReindex.Contains(file))
            {
                vm.SelectedFilesForReindex.Add(file);
            }
        }
        else
        {
            vm.SelectedFilesForReindex.Remove(file);
        }
    }

    private void RelatedFile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm && sender is FrameworkElement element && element.DataContext is SearchResult searchResult)
            {
                // Open the file location in File Explorer
                vm.OpenFileLocationCommand.Execute(searchResult);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in RelatedFile_MouseLeftButtonDown: {ex.Message}");
            System.Windows.MessageBox.Show($"Error opening file location: {ex.Message}", "Error", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double click to maximize/restore
            MaximizeButton_Click(sender, e);
        }
        else
        {
            // Use Windows native drag for proper cursor tracking and snap functionality
            if (WindowState == WindowState.Maximized)
            {
                // For maximized windows, restore first then use native drag
                var mousePos = e.GetPosition(this);
                var screenPos = PointToScreen(mousePos);
                
                // Calculate click position percentage
                var clickPercentX = ActualWidth > 0 ? mousePos.X / ActualWidth : 0.5;
                
                // Restore window
                WindowState = WindowState.Normal;
                
                // Position window so click point stays under cursor
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var workingArea = SystemParameters.WorkArea;
                        var newLeft = screenPos.X - (ActualWidth * clickPercentX);
                        var newTop = screenPos.Y - mousePos.Y;
                        
                        // Clamp to working area
                        newLeft = Math.Max(workingArea.Left, Math.Min(newLeft, workingArea.Right - ActualWidth));
                        newTop = Math.Max(workingArea.Top, Math.Min(newTop, workingArea.Bottom - ActualHeight));
                        
                        Left = newLeft;
                        Top = newTop;
                        
                        // Now use native drag
                        if (_hwndSource != null)
                        {
                            ReleaseCapture();
                            SendMessage(_hwndSource.Handle, WM_SYSCOMMAND, (IntPtr)(SC_MOVE | HT_CAPTION), IntPtr.Zero);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in maximized drag: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
            else
            {
                // Use Windows native drag - maintains cursor position and enables snap
                if (_hwndSource != null)
                {
                    ReleaseCapture();
                    SendMessage(_hwndSource.Handle, WM_SYSCOMMAND, (IntPtr)(SC_MOVE | HT_CAPTION), IntPtr.Zero);
                }
                else
                {
            DragMove();
        }
            }
        }
    }

    private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Windows native drag handles all movement - no custom handling needed
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Windows native drag handles release automatically
        // Ensure rounded corners are maintained
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateWindowCornerRadius();
            UpdateWindowClip();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Settings button clicked - opening settings window");
            Console.WriteLine("Settings button clicked - opening settings window");
            
            // Check if settings window is already open
            if (_settingsWindowInstance != null && _settingsWindowInstance.IsLoaded)
            {
                // Bring existing window to front
                _settingsWindowInstance.Activate();
                _settingsWindowInstance.WindowState = WindowState.Normal;
                _settingsWindowInstance.Focus();
                return;
            }
            
            // Get IConfiguration from App's service provider
            var configuration = ((App)System.Windows.Application.Current).GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            
            if (configuration == null)
            {
                System.Windows.MessageBox.Show("Configuration service is not available.", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }
            
            _settingsWindowInstance = new SettingsWindow(configuration)
            {
                Owner = this,
                Title = "Settings - FileWise"
            };
            
            // Handle window closed event to clear the instance
            _settingsWindowInstance.Closed += (s, args) =>
            {
                _settingsWindowInstance = null;
            };
            
            // Add initial message to console
            _settingsWindowInstance.AppendLog("Console Log initialized. All debug output will appear here.");
            _settingsWindowInstance.AppendLog("This includes Console.WriteLine, Debug.WriteLine, and Trace messages.");
            
            _settingsWindowInstance.Show(); // Use Show() instead of ShowDialog() so it doesn't block
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening settings window: {ex.Message}");
            Console.WriteLine($"Error opening settings window: {ex.Message}");
            System.Windows.MessageBox.Show($"Error opening settings: {ex.Message}\n\nStack trace: {ex.StackTrace}", "Error", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ChatOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is System.Windows.Controls.Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening chat options menu: {ex.Message}");
        }
    }

    private void MainWindow_StateChanged(object sender, EventArgs e)
    {
        UpdateMaximizeButton();
        UpdateWindowCornerRadius();
    }

    private void UpdateMaximizeButton()
    {
        try
        {
            if (MaximizeIconText != null)
            {
                // Use standard Windows Segoe MDL2 Assets icons
                // \uE922 = Maximize, \uE923 = Restore
                if (WindowState == WindowState.Maximized)
                {
                    MaximizeIconText.Text = "\uE923"; // Restore icon
                    if (MaximizeButton != null)
                    {
                        MaximizeButton.ToolTip = "Restore Down";
                    }
                }
                else
                {
                    MaximizeIconText.Text = "\uE922"; // Maximize icon
                    if (MaximizeButton != null)
                    {
                        MaximizeButton.ToolTip = "Maximize";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating maximize button: {ex.Message}");
        }
    }

    private void UpdateWindowCornerRadius()
    {
        if (WindowBorder != null)
        {
            // Rounded corners for Windows 11 style
            if (WindowState == WindowState.Maximized)
            {
                // Square corners when maximized
                WindowBorder.CornerRadius = new CornerRadius(0);
                WindowBorder.Clip = null;
            }
            else
            {
                // Rounded corners when not maximized
                WindowBorder.CornerRadius = new CornerRadius(8);
                
                // Clip the border itself to ensure clean rounded corners
                // Use window dimensions to ensure perfect alignment
                var borderWidth = Math.Max(ActualWidth, RenderSize.Width);
                var borderHeight = Math.Max(ActualHeight, RenderSize.Height);
                
                if (borderWidth > 0 && borderHeight > 0)
                {
                    // Use PathGeometry for more precise clipping
                    WindowBorder.Clip = CreateRoundedRectangleGeometry(borderWidth, borderHeight, 8.0);
                }
            }
            
                if (MainContentGrid != null)
            {
                if (WindowState == WindowState.Maximized)
                {
                    MainContentGrid.Margin = new Thickness(0, 2, 0, 0);
                    MainContentGrid.Clip = null;
                }
                else
                {
                    MainContentGrid.Margin = new Thickness(0);
                    // Clip MainContentGrid to ensure no content protrudes
                    var gridWidth = Math.Max(ActualWidth, RenderSize.Width);
                    var gridHeight = Math.Max(ActualHeight, RenderSize.Height);
                    if (gridWidth > 0 && gridHeight > 0)
                    {
                        // Use PathGeometry for more precise clipping
                        MainContentGrid.Clip = CreateRoundedRectangleGeometry(gridWidth, gridHeight, 8.0);
                    }
                }
            }
            
            UpdateWindowClip();
        }
        UpdateTitleBarMargins();
    }

    private Geometry CreateRoundedRectangleGeometry(double width, double height, double cornerRadius)
    {
        var pathGeometry = new PathGeometry();
        var figure = new PathFigure();
        figure.StartPoint = new Point(cornerRadius, 0);
        
        // Top edge
        figure.Segments.Add(new LineSegment(new Point(width - cornerRadius, 0), true));
        
        // Top-right corner
        figure.Segments.Add(new ArcSegment(
            new Point(width, cornerRadius),
            new Size(cornerRadius, cornerRadius),
            0, false, SweepDirection.Clockwise, true));
        
        // Right edge
        figure.Segments.Add(new LineSegment(new Point(width, height - cornerRadius), true));
        
        // Bottom-right corner
        figure.Segments.Add(new ArcSegment(
            new Point(width - cornerRadius, height),
            new Size(cornerRadius, cornerRadius),
            0, false, SweepDirection.Clockwise, true));
        
        // Bottom edge
        figure.Segments.Add(new LineSegment(new Point(cornerRadius, height), true));
        
        // Bottom-left corner
        figure.Segments.Add(new ArcSegment(
            new Point(0, height - cornerRadius),
            new Size(cornerRadius, cornerRadius),
            0, false, SweepDirection.Clockwise, true));
        
        // Left edge
        figure.Segments.Add(new LineSegment(new Point(0, cornerRadius), true));
        
        // Top-left corner
        figure.Segments.Add(new ArcSegment(
            new Point(cornerRadius, 0),
            new Size(cornerRadius, cornerRadius),
            0, false, SweepDirection.Clockwise, true));
        
        figure.IsClosed = true;
        pathGeometry.Figures.Add(figure);
        
        return pathGeometry;
    }

    private void UpdateWindowClip()
    {
        try
        {
            if (WindowState == WindowState.Maximized)
            {
                // No clipping when maximized
                Clip = null;
                if (WindowBorder != null)
                {
                    WindowBorder.Clip = null;
                }
                if (ContentClipBorder != null)
                {
                    ContentClipBorder.Clip = null;
                }
                if (MainContentGrid != null)
                {
                    MainContentGrid.Clip = null;
                }
            }
            else
            {
                // Apply rounded corner clipping when not maximized
                var cornerRadius = 8.0;
                var width = Math.Max(ActualWidth, RenderSize.Width);
                var height = Math.Max(ActualHeight, RenderSize.Height);
                
                if (width > 0 && height > 0)
                {
                    // Clip the window itself - transparent background, so any protrusion will be transparent
                    Clip = CreateRoundedRectangleGeometry(width, height, cornerRadius);
                    
                    // WindowBorder is now transparent, but clip it anyway for consistency
                    if (WindowBorder != null)
                    {
                        WindowBorder.Clip = CreateRoundedRectangleGeometry(width, height, cornerRadius);
                    }
                    
                    // Clip ContentClipBorder (which has the white background) - make it slightly smaller to prevent white corners
                    if (ContentClipBorder != null)
                    {
                        // Make ContentClipBorder slightly smaller than the window to prevent white corners
                        // Use a small inset (1 pixel) to ensure no white shows at corners
                        var inset = 0.5;
                        var contentWidth = Math.Max(width - (inset * 2), 0);
                        var contentHeight = Math.Max(height - (inset * 2), 0);
                        
                        if (contentWidth > 0 && contentHeight > 0)
                        {
                            var contentClip = CreateRoundedRectangleGeometry(contentWidth, contentHeight, cornerRadius);
                            // Translate the clip to account for the inset
                            var transform = new TranslateTransform(inset, inset);
                            contentClip.Transform = transform;
                            ContentClipBorder.Clip = contentClip;
                        }
                        else
                        {
                            // Fallback to full window size if dimensions aren't ready
                            ContentClipBorder.Clip = CreateRoundedRectangleGeometry(width, height, cornerRadius);
                        }
                    }
                    
                    // Ensure MainContentGrid is also clipped
                    if (MainContentGrid != null)
                    {
                        MainContentGrid.Clip = CreateRoundedRectangleGeometry(width, height, cornerRadius);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash the app
            System.Diagnostics.Debug.WriteLine($"Error updating window clip: {ex.Message}");
        }
    }

    private void UpdateTitleBarMargins()
    {
        if (WindowState == WindowState.Maximized)
        {
            // When maximized, use minimal margins (Windows standard)
            if (AppIcon != null)
                AppIcon.Margin = new Thickness(12, 0, 8, 0);
            if (SearchBarBorder != null)
                SearchBarBorder.Margin = new Thickness(0);
        }
        else
        {
            // When not maximized, add more margin to account for window border
            if (AppIcon != null)
                AppIcon.Margin = new Thickness(12, 0, 8, 0);
            if (SearchBarBorder != null)
                SearchBarBorder.Margin = new Thickness(0);
        }
    }
}

