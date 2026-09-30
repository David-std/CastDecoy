using CastDecoy.Services;
using FlashCap;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace CastDecoy;

public partial class MainWindow : Window
{
    private readonly List<WindowInfo> _allWindows = new();
    private readonly ObservableCollection<WindowInfo> _displayedWindows = new();
    private readonly ObservableCollection<AppShortcutItem> _appShortcuts = new();
    private readonly object _appShortcutsLock = new();
    private readonly Dictionary<string, HotkeyBinding> _savedAppHotkeys = new(StringComparer.OrdinalIgnoreCase);

    private readonly DispatcherTimer _syncTimer;
    private readonly DispatcherTimer _autoDetectTimer;
    private readonly uint _selfPid;
    private IntPtr _selfHwnd;
    private WinForms.NotifyIcon? _trayIcon;
    private bool _isExiting;
    private ITaskbarList? _taskbarList;
    private CancellationTokenSource? _livePreviewCts;
    private int _currentTab = 1;

    private static readonly SolidColorBrush ActiveTextBrush = new(MediaColor.FromRgb(0x4f, 0x83, 0xf5));
    private static readonly SolidColorBrush InactiveTextBrush = new(MediaColor.FromRgb(0x47, 0x55, 0x69));
    private static readonly SolidColorBrush AccentBlueBrush = new(MediaColor.FromRgb(0x4f, 0x83, 0xf5));
    private static readonly SolidColorBrush StatusGreenBrush = new(MediaColor.FromRgb(0x4f, 0x83, 0xf5));
    private static readonly SolidColorBrush StatusRedBrush = new(MediaColor.FromRgb(0xdc, 0x26, 0x26));
    private static readonly SolidColorBrush StatusAmberBrush = new(MediaColor.FromRgb(0xd9, 0x77, 0x06));
    private static readonly SolidColorBrush TileActiveBorderBrush = new(MediaColor.FromRgb(0x4f, 0x83, 0xf5));
    private static readonly SolidColorBrush TileNormalBorderBrush = new(MediaColor.FromRgb(0xe2, 0xe8, 0xf0));
    private static readonly SolidColorBrush TileActiveBgBrush = new(MediaColor.FromRgb(0xf4, 0xf8, 0xfe));
    private static readonly SolidColorBrush TileNormalBgBrush = new(MediaColor.FromRgb(0xff, 0xff, 0xff));

    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "DV2ControlHost", "Windows.UI.Core.CoreWindow", "Shell_InputSwitchTopLevelWindow",
        "XamlExplorerHostIslandWindow", "Windows.Internal.Shell.TabProxyWindow",
        "MultitaskingViewFrame", "ForegroundStaging", "EdgeUiInputTopWndClass",
        "NativeHWNDHost", "TaskListThumbnailWnd", "WindowsDashboard",
        "ApplicationFrameTitleBarWindow", "TopLevelWindowForOverflowXamlIsland",
        "Chrome_WidgetWin_0", "MSCTFIME UI", "IME", "Default IME",
        "OleMainThreadWndClass", "SysListView32", "CEF-OSC-WIDGET",
        "DWM Notification Window", "DummyWindow", "GDI+ Hook Window Class"
    };

    public MainWindow()
    {
        InitializeComponent();
        NativeMethods.EnableSeDebugPrivilege();
        try
        {
            string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icon.ico");
            if (!System.IO.File.Exists(icoPath))
                icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (System.IO.File.Exists(icoPath))
            {
                Icon = BitmapFrame.Create(new Uri(icoPath, UriKind.Absolute));
            }
        }
        catch { }

        WindowListBox.ItemsSource = _displayedWindows;
        AppShortcutsListBox.ItemsSource = _appShortcuts;
        _selfPid = (uint)Environment.ProcessId;

        DetectDefaultBrowser();

        try
        {
            _taskbarList = (ITaskbarList)new CoTaskbarList();
            _taskbarList.HrInit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ITaskbarList init failed: {ex.Message}");
        }

        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _syncTimer.Tick += (_, _) => SyncAffinityAndStealth();

        _autoDetectTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _autoDetectTimer.Tick += (_, _) => RefreshWindowsSilent();

        Loaded += (_, _) =>
        {
            try
            {
                _selfHwnd = new WindowInteropHelper(this).EnsureHandle();
                bool isPreview = Environment.GetCommandLineArgs().Any(a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
                if (!isPreview)
                {
                    SetSelfDisplayAffinity(true);
                    SelfHideSwitch.IsChecked = true;
                    ApplyTaskbarOption(_selfHwnd, true);
                }
                else
                {
                    SetSelfDisplayAffinity(false);
                    SelfHideSwitch.IsChecked = false;
                    ApplyTaskbarOption(_selfHwnd, false);
                }

                InitElevationStatus();
                InstallGlobalKeyboardHook();
                InitTrayIcon();
                RefreshWindows();
                _syncTimer.Start();
                _autoDetectTimer.Start();
                UpdateActionButtons();
                InitMonitorInfo();
                StartLivePreviewLoop();
                UpdateAllHotkeysVisuals();
                InitCameraControls();
                InitMicrophoneControls();
                InitMouseControls();

                if (isPreview)
                {
                    bool testOpera = Environment.GetCommandLineArgs().Any(a => a.Equals("--test-opera", StringComparison.OrdinalIgnoreCase));
                    bool testDiag = Environment.GetCommandLineArgs().Any(a => a.Equals("--test-diag", StringComparison.OrdinalIgnoreCase));
                    bool testDiagOn = Environment.GetCommandLineArgs().Any(a => a.Equals("--test-diag-on", StringComparison.OrdinalIgnoreCase));
                    bool testDiagPartial = Environment.GetCommandLineArgs().Any(a => a.Equals("--test-diag-partial", StringComparison.OrdinalIgnoreCase));
                    bool testMic = Environment.GetCommandLineArgs().Any(a => a.Equals("--test-mic", StringComparison.OrdinalIgnoreCase));
                    bool testMouse = Environment.GetCommandLineArgs().Any(a => a.Equals("--test-mouse", StringComparison.OrdinalIgnoreCase));
                    bool testHotkeys = Environment.GetCommandLineArgs().Any(a => a.Equals("--test-hotkeys", StringComparison.OrdinalIgnoreCase));
                    Dispatcher.InvokeAsync(async () =>
                    {
                        await Task.Delay(2000);
                        if (testMic)
                        {
                            SwitchTab(4);
                            await Task.Delay(300);
                        }
                        else if (testMouse)
                        {
                            SwitchTab(2);
                            await Task.Delay(300);
                        }
                        else if (testHotkeys)
                        {
                            SwitchTab(5);
                            await Task.Delay(300);
                        }
                        if (testOpera && ChipOpera != null)
                        {
                            ChipOpera.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            await Task.Delay(300);
                        }
                        if ((testDiag || testDiagOn || testDiagPartial) && DiagnosticRow != null)
                        {
                            DiagnosticRow.Visibility = Visibility.Visible;
                            UpdateLiveDiagnosticStatus();
                            await Task.Delay(300);
                        }
                        try
                        {
                            int w = (int)ActualWidth;
                            int h = (int)ActualHeight;
                            if (w > 0 && h > 0)
                            {
                                var rtb = new RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                                rtb.Render(this);
                                var encoder = new PngBitmapEncoder();
                                encoder.Frames.Add(BitmapFrame.Create(rtb));
                                string fileName = testHotkeys ? "hotkeys_tab_preview.png" : (testMouse ? "mouse_tab_preview.png" : (testMic ? "mic_tab_preview.png" : (testDiagOn ? "diagnostic_card_on_preview.png" : (testDiagPartial ? "diagnostic_card_partial_preview.png" : (testDiag ? "diagnostic_card_preview.png" : (testOpera ? "opera_selected_preview.png" : "browser_chips_preview.png"))))));
                                string outPath = System.IO.Path.Combine(@"C:\Users\david\.gemini\antigravity-ide\brain\d7697bb7-bcbd-4c19-a1e3-9da4624e79d9", fileName);
                                using var fs = File.Create(outPath);
                                encoder.Save(fs);
                            }
                        }
                        catch { }
                        Application.Current.Shutdown();
                    }, DispatcherPriority.ApplicationIdle);
                }
            }
            catch { }
        };

        Closing += (_, e) =>
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                CleanupOnExit();
            }
        };

        Closed += (_, _) =>
        {
            _syncTimer.Stop();
            _autoDetectTimer.Stop();
            _livePreviewCts?.Cancel();
            _trayIcon?.Dispose();
            UninstallGlobalKeyboardHook();
            RestoreRealCursor();
            CleanupCamera();
            _audioService?.Dispose();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            CleanupOnExit();
        };
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            RefreshWindows();
            e.Handled = true;
        }
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        Close();
    }

    
    private void OnHelpCircleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.ToolTip is string tip)
        {
            MessageBox.Show(tip, "Información", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    
    private void InitTrayIcon()
    {
        System.Drawing.Icon trayIco = System.Drawing.SystemIcons.Shield;
        try
        {
            var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"));
            if (sri?.Stream != null)
            {
                trayIco = new System.Drawing.Icon(sri.Stream);
            }
            else
            {
                string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icon.ico");
                if (!System.IO.File.Exists(icoPath))
                    icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (System.IO.File.Exists(icoPath))
                {
                    trayIco = new System.Drawing.Icon(icoPath);
                }
            }
        }
        catch { }

        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "CastDecoy",
            Visible = true,
            Icon = trayIco
        };

        _trayIcon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        BuildTrayContextMenu();
    }

    
    private void BuildTrayContextMenu()
    {
        if (_trayIcon == null) return;

        var menu = new WinForms.ContextMenuStrip();

        var headerItem = new WinForms.ToolStripMenuItem("CastDecoy")
        {
            Enabled = false,
            Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold)
        };
        menu.Items.Add(headerItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var isolated = _allWindows.Where(w => w.IsGhosted).ToList();
        if (isolated.Count > 0)
        {
            var sectionLabel = new WinForms.ToolStripMenuItem($"Ventanas aisladas ({isolated.Count}):")
            {
                Enabled = false
            };
            menu.Items.Add(sectionLabel);

            foreach (var win in isolated)
            {
                string label = win.CleanTitle;

                var winItem = new WinForms.ToolStripMenuItem($"  {label}", null, (_, _) =>
                {
                    FocusWindow(win.Handle);
                });
                winItem.ToolTipText = $"Traer al frente: {win.Title}";
                menu.Items.Add(winItem);
            }

            menu.Items.Add(new WinForms.ToolStripSeparator());
        }

        menu.Items.Add("Abrir CastDecoy", null, (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });

        menu.Items.Add("Restaurar todas", null, (_, _) => SetAllGhost(false));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => { _isExiting = true; Close(); });

        _trayIcon.ContextMenuStrip = menu;
    }

    
    private void OnStateChanged(object sender, EventArgs e)
    {
    }

    
    private void CleanupOnExit()
    {
        RestoreRealCursor();
        CleanupCamera();
        UninstallGlobalKeyboardHook();
        if (_taskbarList != null)
        {
            foreach (var win in _allWindows)
            {
                if (win.IsGhosted)
                {
                    try { _taskbarList.AddTab(win.Handle); } catch { }
                }
            }
            try { _taskbarList.AddTab(_selfHwnd); } catch { }
        }
    }

    
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    
    public static void RestartAsAdmin()
    {
        try
        {
            string exePath = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            System.Diagnostics.Process.Start(psi);
            System.Windows.Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC prompt
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo reiniciar como Administrador: {ex.Message}", "CastDecoy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    
    private void InitElevationStatus()
    {
        bool isAdmin = IsAdministrator();
        if (AdminElevateBtn != null)
        {
            if (isAdmin)
            {
                AdminElevateBtn.Content = "🛡️ Administrador activo";
                AdminElevateBtn.IsEnabled = false;
                AdminElevateBtn.Opacity = 0.75;
                AdminElevateBtn.ToolTip = "CastDecoy cuenta con permisos de Administrador y puede aislar cualquier ventana del sistema.";
            }
            else
            {
                AdminElevateBtn.Content = "🛡️ Permisos de Administrador";
                AdminElevateBtn.IsEnabled = true;
                AdminElevateBtn.Opacity = 1.0;
                AdminElevateBtn.ToolTip = "Haz clic aquí para reiniciar con permisos de Administrador y poder ocultar el Administrador de tareas u otras ventanas del sistema.";
            }
        }
    }

    
    private void OnElevateAdminClick(object sender, RoutedEventArgs e)
    {
        var prompt = MessageBox.Show(
            "¿Deseas reiniciar CastDecoy con permisos de Administrador?\n\nEsto permitirá aislar ventanas elevadas del sistema (como el Administrador de tareas, terminales de administración, etc.) de las grabaciones de pantalla.",
            "Ejecutar como Administrador",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (prompt == MessageBoxResult.Yes)
        {
            RestartAsAdmin();
        }
    }

    
    private void SwitchTab(int tabIndex)
    {
        _currentTab = tabIndex;
        Tab1Indicator.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        Tab2Indicator.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        Tab3Indicator.Visibility = tabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        Tab4Indicator.Visibility = tabIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        Tab5Indicator.Visibility = tabIndex == 5 ? Visibility.Visible : Visibility.Collapsed;

        Tab1Text.Foreground = tabIndex == 1 ? ActiveTextBrush : InactiveTextBrush;
        Tab1Text.FontWeight = tabIndex == 1 ? FontWeights.SemiBold : FontWeights.Normal;
        Tab2Text.Foreground = tabIndex == 2 ? ActiveTextBrush : InactiveTextBrush;
        Tab2Text.FontWeight = tabIndex == 2 ? FontWeights.SemiBold : FontWeights.Normal;
        Tab3Text.Foreground = tabIndex == 3 ? ActiveTextBrush : InactiveTextBrush;
        Tab3Text.FontWeight = tabIndex == 3 ? FontWeights.SemiBold : FontWeights.Normal;
        Tab4Text.Foreground = tabIndex == 4 ? ActiveTextBrush : InactiveTextBrush;
        Tab4Text.FontWeight = tabIndex == 4 ? FontWeights.SemiBold : FontWeights.Normal;
        Tab5Text.Foreground = tabIndex == 5 ? ActiveTextBrush : InactiveTextBrush;
        Tab5Text.FontWeight = tabIndex == 5 ? FontWeights.SemiBold : FontWeights.Normal;

        Tab1Content.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        Tab2Content.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        Tab3Content.Visibility = tabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        Tab4Content.Visibility = tabIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        Tab5Content.Visibility = tabIndex == 5 ? Visibility.Visible : Visibility.Collapsed;

        Tab1FooterBorder.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;

        if (tabIndex == 2)
        {
            if (!_isMouseRecording && !_isMousePlaying && _recordedMouseTrack.Count == 0)
            {
                CenterMousepadDot();
            }
        }
        else if (tabIndex == 5)
        {
            SyncAppShortcuts();
        }
    }

    
    private void OnTab1Click(object sender, MouseButtonEventArgs e) => SwitchTab(1);

        private void OnTab2Click(object sender, MouseButtonEventArgs e) => SwitchTab(2);

        private void OnTab3Click(object sender, MouseButtonEventArgs e) => SwitchTab(3);

        private void OnTab4Click(object sender, MouseButtonEventArgs e) => SwitchTab(4);

        private void OnTab5Click(object sender, MouseButtonEventArgs e) => SwitchTab(5);
}
