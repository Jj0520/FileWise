using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using FileWise.Models;
using FileWise.Services;
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
            
            // Subscribe to language changes to update UI strings
            LocalizationService.Instance.LanguageChanged += LocalizationService_LanguageChanged;
            
            // Initial UI strings update
            UpdateUIStrings();
            
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
                
                // Handle processing animation
                viewModel.PropertyChanged += (s, e) =>
                {
                    try
                    {
                        if (e.PropertyName == nameof(MainViewModel.IsProcessingQuery))
                        {
                            if (viewModel.IsProcessingQuery)
                            {
                                UpdateProcessingAnimation();
                            }
                            else
                            {
                                StopProcessingAnimation();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error handling IsProcessingQuery change: {ex.Message}");
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
                                // Scroll to bottom when chat panel opens
                                if (viewModel.IsChatPanelOpen && ChatScrollViewer != null)
                                {
                                    ChatScrollViewer.ScrollToEnd();
                                }
                                
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

    private void NetworkPath_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement element)
            {
                var path = element.Tag as string;
                if (string.IsNullOrEmpty(path))
                {
                    // Try to get path from converter
                    var textBlock = element.FindName("PathTextBlock") as System.Windows.Controls.TextBlock;
                    if (textBlock != null)
                    {
                        path = textBlock.Text;
                    }
                }

                if (!string.IsNullOrEmpty(path))
                {
                    // Open the network path as a folder in Windows Explorer
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in NetworkPath_MouseLeftButtonDown: {ex.Message}");
            System.Windows.MessageBox.Show($"Error opening folder: {ex.Message}", "Error", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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

    public void OpenSettingsWindow()
    {
        SettingsButton_Click(null, null);
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
                if (ContentClipBorder != null)
                {
                    ContentClipBorder.CornerRadius = new CornerRadius(0);
                    ContentClipBorder.Margin = new Thickness(0);
                }
                if (TitleBarBorder != null)
                {
                    TitleBarBorder.CornerRadius = new CornerRadius(0);
                }
            }
            else
            {
                // Check if window is snapped to left or right edge
                var isSnappedLeft = IsWindowSnappedToLeft();
                var isSnappedRight = IsWindowSnappedToRight();
                
                // Conditional corner radius based on snapping
                // Snapped to left: remove left corners (top-left, bottom-left)
                // Snapped to right: remove right corners (top-right, bottom-right)
                // Not snapped: all corners rounded
                CornerRadius cornerRadius;
                if (isSnappedLeft)
                {
                    cornerRadius = new CornerRadius(0, 8, 0, 8); // No left corners
                }
                else if (isSnappedRight)
                {
                    cornerRadius = new CornerRadius(8, 0, 8, 0); // No right corners
                }
                else
                {
                    cornerRadius = new CornerRadius(8); // All corners rounded
                }
                
                WindowBorder.CornerRadius = cornerRadius;
                
                // Update ContentClipBorder corner radius to match
                if (ContentClipBorder != null)
                {
                    // When snapped, remove corner radius on rounded side to prevent clipping
                    // This allows the background to extend fully to cover rounded corners
                    if (isSnappedLeft)
                    {
                        // Snapped to left: remove right corners (rounded side) from ContentClipBorder
                        // so background extends fully to cover the window's rounded corners
                        ContentClipBorder.CornerRadius = new CornerRadius(0, 0, 0, 0); // No corners on ContentClipBorder
                    }
                    else if (isSnappedRight)
                    {
                        // Snapped to right: remove left corners (rounded side) from ContentClipBorder
                        ContentClipBorder.CornerRadius = new CornerRadius(0, 0, 0, 0); // No corners on ContentClipBorder
                    }
                    else
                    {
                        // Not snapped: use matching corner radius
                        ContentClipBorder.CornerRadius = cornerRadius;
                    }
                    
                    // Reset margin and transforms
                    ContentClipBorder.Margin = new Thickness(0);
                    ContentClipBorder.RenderTransform = null;
                }
                
                // Update TitleBar corner radius (only top corners matter)
                if (TitleBarBorder != null)
                {
                    var titleBarCornerRadius = new CornerRadius(
                        cornerRadius.TopLeft,  // Top-left
                        cornerRadius.TopRight, // Top-right
                        0,                      // Bottom-right (always 0)
                        0                       // Bottom-left (always 0)
                    );
                    TitleBarBorder.CornerRadius = titleBarCornerRadius;
                }
                
                // Clip the border itself to ensure clean rounded corners
                // Use window dimensions to ensure perfect alignment
                var borderWidth = Math.Max(ActualWidth, RenderSize.Width);
                var borderHeight = Math.Max(ActualHeight, RenderSize.Height);
                
                if (borderWidth > 0 && borderHeight > 0)
                {
                    // Use PathGeometry for more precise clipping with conditional corners
                    WindowBorder.Clip = CreateRoundedRectangleGeometry(borderWidth, borderHeight, cornerRadius);
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
                        // Check if window is snapped to left or right edge
                        var isSnappedLeft = IsWindowSnappedToLeft();
                        var isSnappedRight = IsWindowSnappedToRight();
                        
                        CornerRadius cornerRadius;
                        if (isSnappedLeft)
                        {
                            cornerRadius = new CornerRadius(0, 8, 0, 8); // No left corners
                        }
                        else if (isSnappedRight)
                        {
                            cornerRadius = new CornerRadius(8, 0, 8, 0); // No right corners
                        }
                        else
                        {
                            cornerRadius = new CornerRadius(8); // All corners rounded
                        }
                        
                        // Use PathGeometry for more precise clipping with conditional corners
                        MainContentGrid.Clip = CreateRoundedRectangleGeometry(gridWidth, gridHeight, cornerRadius);
                    }
                }
            }
            
            UpdateWindowClip();
        }
        UpdateTitleBarMargins();
    }

    private bool IsWindowSnappedToLeft()
    {
        if (WindowState == WindowState.Maximized)
            return false;
        
        try
        {
            var workingArea = SystemParameters.WorkArea;
            var leftEdge = workingArea.Left;
            var tolerance = 5.0; // Allow 5px tolerance for edge detection
            
            // Check if window is touching or very close to the left edge
            return Math.Abs(Left - leftEdge) <= tolerance;
        }
        catch
        {
            return false;
        }
    }
    
    private bool IsWindowSnappedToRight()
    {
        if (WindowState == WindowState.Maximized)
            return false;
        
        try
        {
            var workingArea = SystemParameters.WorkArea;
            var rightEdge = workingArea.Right;
            var tolerance = 5.0; // Allow 5px tolerance for edge detection
            var windowRight = Left + ActualWidth;
            
            // Check if window is touching or very close to the right edge
            return Math.Abs(windowRight - rightEdge) <= tolerance;
        }
        catch
        {
            return false;
        }
    }

    private Geometry CreateRoundedRectangleGeometry(double width, double height, CornerRadius cornerRadius)
    {
        var pathGeometry = new PathGeometry();
        var figure = new PathFigure();
        
        var topLeft = cornerRadius.TopLeft;
        var topRight = cornerRadius.TopRight;
        var bottomRight = cornerRadius.BottomRight;
        var bottomLeft = cornerRadius.BottomLeft;
        
        figure.StartPoint = new Point(topLeft, 0);
        
        // Top edge
        figure.Segments.Add(new LineSegment(new Point(width - topRight, 0), true));
        
        // Top-right corner
        if (topRight > 0)
        {
        figure.Segments.Add(new ArcSegment(
                new Point(width, topRight),
                new Size(topRight, topRight),
            0, false, SweepDirection.Clockwise, true));
        }
        else
        {
            figure.Segments.Add(new LineSegment(new Point(width, 0), true));
        }
        
        // Right edge
        figure.Segments.Add(new LineSegment(new Point(width, height - bottomRight), true));
        
        // Bottom-right corner
        if (bottomRight > 0)
        {
        figure.Segments.Add(new ArcSegment(
                new Point(width - bottomRight, height),
                new Size(bottomRight, bottomRight),
            0, false, SweepDirection.Clockwise, true));
        }
        else
        {
            figure.Segments.Add(new LineSegment(new Point(width, height), true));
        }
        
        // Bottom edge
        figure.Segments.Add(new LineSegment(new Point(bottomLeft, height), true));
        
        // Bottom-left corner
        if (bottomLeft > 0)
        {
        figure.Segments.Add(new ArcSegment(
                new Point(0, height - bottomLeft),
                new Size(bottomLeft, bottomLeft),
            0, false, SweepDirection.Clockwise, true));
        }
        else
        {
            figure.Segments.Add(new LineSegment(new Point(0, height), true));
        }
        
        // Left edge
        figure.Segments.Add(new LineSegment(new Point(0, topLeft), true));
        
        // Top-left corner
        if (topLeft > 0)
        {
        figure.Segments.Add(new ArcSegment(
                new Point(topLeft, 0),
                new Size(topLeft, topLeft),
            0, false, SweepDirection.Clockwise, true));
        }
        else
        {
            figure.Segments.Add(new LineSegment(new Point(0, 0), true));
        }
        
        figure.IsClosed = true;
        pathGeometry.Figures.Add(figure);
        
        return pathGeometry;
    }
    
    // Overload for backward compatibility with single corner radius value
    private Geometry CreateRoundedRectangleGeometry(double width, double height, double cornerRadius)
    {
        return CreateRoundedRectangleGeometry(width, height, new CornerRadius(cornerRadius));
    }

    private void UpdateWindowClip()
    {
        try
        {
            if (WindowState == WindowState.Maximized)
            {
                // Reset window background to transparent when maximized
                Background = System.Windows.Media.Brushes.Transparent;
                
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
                // Check if window is snapped to left or right edge
                var isSnappedLeft = IsWindowSnappedToLeft();
                var isSnappedRight = IsWindowSnappedToRight();
                
                CornerRadius cornerRadius;
                if (isSnappedLeft)
                {
                    cornerRadius = new CornerRadius(0, 8, 0, 8); // No left corners
                }
                else if (isSnappedRight)
                {
                    cornerRadius = new CornerRadius(8, 0, 8, 0); // No right corners
                }
                else
                {
                    cornerRadius = new CornerRadius(8); // All corners rounded
                }
                
                var width = Math.Max(ActualWidth, RenderSize.Width);
                var height = Math.Max(ActualHeight, RenderSize.Height);
                
                if (width > 0 && height > 0)
                {
                    // When snapped, set window background to match ContentClipBorder to prevent white showing
                    // through at rounded corners where the window clip cuts off ContentClipBorder
                    if (isSnappedLeft || isSnappedRight)
                    {
                        // Set window background to match ContentClipBorder background color
                        // This ensures that when the window clip cuts off ContentClipBorder at rounded corners,
                        // we see the same color instead of white
                        var contentBackgroundBrush = TryFindResource("ContentBackgroundBrush") as System.Windows.Media.Brush;
                        if (contentBackgroundBrush != null)
                        {
                            Background = contentBackgroundBrush;
                        }
                    }
                    else
                    {
                        // When not snapped, keep window background transparent
                        Background = System.Windows.Media.Brushes.Transparent;
                    }
                    
                    // Clip the window itself - transparent background, so any protrusion will be transparent
                    Clip = CreateRoundedRectangleGeometry(width, height, cornerRadius);
                    
                    // WindowBorder is now transparent, but clip it anyway for consistency
                    if (WindowBorder != null)
                    {
                        WindowBorder.Clip = CreateRoundedRectangleGeometry(width, height, cornerRadius);
                    }
                    
                    // Clip ContentClipBorder (which has the white/colored background)
                    if (ContentClipBorder != null)
                    {
                        // When snapped, don't clip ContentClipBorder - let it fill the entire window
                        // The window's clip will handle the rounded corners, and the background
                        // will fully cover the rounded areas without white showing through
                        if (isSnappedLeft || isSnappedRight)
                        {
                            // Remove clipping to let background extend fully to cover rounded corners
                            ContentClipBorder.Clip = null;
                        }
                        else
                        {
                            // When not snapped, use a small inset to prevent white corners
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
                    }
                    
                    // Ensure MainContentGrid is also clipped (already handled in UpdateWindowCornerRadius, but keep for consistency)
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


    private void CompressPdfMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var configuration = ((App)System.Windows.Application.Current).GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            if (configuration != null)
            {
                var compressWindow = new CompressPdfWindow
                {
                    Owner = this,
                    Title = "Compress PDF Files - FileWise"
                };
                compressWindow.Show();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening compress PDF window: {ex.Message}");
            System.Windows.MessageBox.Show($"Error opening compress PDF window: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void EditMenuButton_Click(object sender, RoutedEventArgs e)
    {
        // The context menu will open automatically when the button is clicked
        // This handler is just to satisfy the XAML binding
    }

    private void UndoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplicationCommands.Undo.Execute(null, this);
    }

    private void RedoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplicationCommands.Redo.Execute(null, this);
    }

    private void CutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplicationCommands.Cut.Execute(null, this);
    }

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplicationCommands.Copy.Execute(null, this);
    }

    private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplicationCommands.Paste.Execute(null, this);
    }

    private void FindMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplicationCommands.Find.Execute(null, this);
    }

    private void UpdateProcessingAnimation()
    {
        try
        {
            if (ProcessingStatusTextBlock == null)
                return;

            // Get localized strings
            var thinking = LocalizationService.Instance.GetString("Animation_Thinking");
            var planning = LocalizationService.Instance.GetString("Animation_Planning");
            var generating = LocalizationService.Instance.GetString("Animation_GeneratingResponse");

            // Create animation with localized strings and animated ellipsis
            var animation = new System.Windows.Media.Animation.StringAnimationUsingKeyFrames();
            animation.Duration = TimeSpan.FromSeconds(4.5); // 1.5 seconds per state (3 frames each)
            animation.RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever;

            // Thinking with animated dots (0.0s, 0.5s, 1.0s)
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(thinking, TimeSpan.FromSeconds(0.0)));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(thinking + ".", TimeSpan.FromSeconds(0.5)));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(thinking + "..", TimeSpan.FromSeconds(1.0)));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(thinking + "...", TimeSpan.FromSeconds(1.5)));

            // Planning with animated dots (1.5s, 2.0s, 2.5s)
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(planning, TimeSpan.FromSeconds(1.5)));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(planning + ".", TimeSpan.FromSeconds(2.0)));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(planning + "..", TimeSpan.FromSeconds(2.5)));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(planning + "...", TimeSpan.FromSeconds(3.0)));

            // Generating Response with animated dots (3.0s, 3.5s, 4.0s)
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(generating, TimeSpan.FromSeconds(3.0)));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(generating + ".", TimeSpan.FromSeconds(3.5)));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(generating + "..", TimeSpan.FromSeconds(4.0)));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteStringKeyFrame(generating + "...", TimeSpan.FromSeconds(4.5)));

            // Start the animation
            ProcessingStatusTextBlock.BeginAnimation(System.Windows.Controls.TextBlock.TextProperty, animation);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating processing animation: {ex.Message}");
        }
    }

    private void StopProcessingAnimation()
    {
        try
        {
            if (ProcessingStatusTextBlock == null)
                return;

            // Stop the animation and clear the text
            ProcessingStatusTextBlock.BeginAnimation(System.Windows.Controls.TextBlock.TextProperty, null);
            ProcessingStatusTextBlock.Text = string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error stopping processing animation: {ex.Message}");
        }
    }

    private void ImageUploadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp|All Files|*.*",
                Multiselect = false,
                Title = "Select Image to Upload"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                // TODO: Implement image upload functionality
                System.Diagnostics.Debug.WriteLine($"Image selected: {openFileDialog.FileName}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in ImageUploadButton_Click: {ex.Message}");
            System.Windows.MessageBox.Show($"Error selecting image: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
    {
        // Update UI strings when language changes
        Dispatcher.Invoke(() => UpdateUIStrings());
        
        // If processing animation is running, restart it with new language
        if (DataContext is MainViewModel vm && vm.IsProcessingQuery)
        {
            UpdateProcessingAnimation();
        }
    }

    private void UpdateUIStrings()
    {
        try
        {
            var loc = LocalizationService.Instance;
            
            // Update menu buttons
            if (FileMenuButton != null)
                FileMenuButton.Content = loc.GetString("Menu_File");
            if (EditMenuButton != null)
                EditMenuButton.Content = loc.GetString("Menu_Edit");
            
            // Update folder selection label
            if (FolderSelectionLabel != null)
                FolderSelectionLabel.Text = loc.GetString("Label_FolderSelection");
            
            // Update select folder button
            if (SelectFolderButton != null)
                SelectFolderButton.Content = loc.GetString("Button_SelectFolder");
            
            // Update search label
            if (SearchLabel != null)
                SearchLabel.Text = loc.GetString("Button_Search");
            
            // Update sort buttons
            if (SortByNameButton != null)
                SortByNameButton.Content = loc.GetString("Button_Sort_Name");
            if (SortByTypeButton != null)
                SortByTypeButton.Content = loc.GetString("Button_Sort_Type");
            if (SortBySizeButton != null)
                SortBySizeButton.Content = loc.GetString("Button_Sort_Size");
            if (SortByDateButton != null)
                SortByDateButton.Content = loc.GetString("Button_Sort_Date");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating UI strings: {ex.Message}");
        }
    }
}

