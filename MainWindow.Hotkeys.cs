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
    public static bool IsCapturingHotkey { get; set; } = false;
    private readonly HotkeyBinding _selfHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x48 };
    private readonly HotkeyBinding _freezeHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x46 };
    private readonly HotkeyBinding _mousePlayHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x52 };
    private readonly HotkeyBinding _jigglerHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x4A };
    private readonly HotkeyBinding _camHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x43 };
    private readonly HotkeyBinding _camToggleHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x56 };
    private readonly HotkeyBinding _camLagHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x4C };
    private HotkeyBinding _micHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x4D };
    private readonly HotkeyBinding _micToggleHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x55 };
    private readonly HotkeyBinding _micLagHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x4B };
    private int _mouseHotkeyIndex = 0;
    private int _camHotkeyIndex = 0;
    private int _micHotkeyIndex = 0;
    private NativeMethods.LowLevelProc? _keyboardHookProc;
    private IntPtr _keyboardHookHandle = IntPtr.Zero;

    
    private void InstallGlobalKeyboardHook()
    {
        if (_keyboardHookHandle != IntPtr.Zero) return;

        try
        {
            _keyboardHookProc = KeyboardHookCallback;
            IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
            _keyboardHookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardHookProc, moduleHandle, 0);
            if (_keyboardHookHandle == IntPtr.Zero)
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                IntPtr hMod = curModule != null ? NativeMethods.GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;
                _keyboardHookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardHookProc, hMod, 0);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to install keyboard hook: {ex.Message}");
        }
    }

    
    private void UninstallGlobalKeyboardHook()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            try { NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle); } catch { }
            _keyboardHookHandle = IntPtr.Zero;
        }
    }

    
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (IsCapturingHotkey)
        {
            return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            try
            {
                var kbd = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                uint vk = kbd.vkCode;

                if (vk != NativeMethods.VK_CONTROL && vk != NativeMethods.VK_SHIFT && vk != NativeMethods.VK_MENU &&
                    vk != 0xA0 && vk != 0xA1 && vk != 0xA2 && vk != 0xA3 && vk != 0xA4 && vk != 0xA5)
                {
                    bool ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0 ||
                                (NativeMethods.GetAsyncKeyState(0xA2) & 0x8000) != 0 ||
                                (NativeMethods.GetAsyncKeyState(0xA3) & 0x8000) != 0;
                    bool shift = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0 ||
                                 (NativeMethods.GetAsyncKeyState(0xA0) & 0x8000) != 0 ||
                                 (NativeMethods.GetAsyncKeyState(0xA1) & 0x8000) != 0;
                    bool alt = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0 ||
                               (NativeMethods.GetAsyncKeyState(0xA4) & 0x8000) != 0 ||
                               (NativeMethods.GetAsyncKeyState(0xA5) & 0x8000) != 0 ||
                               ((kbd.flags & 0x20) != 0);

                    if (_selfHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(ToggleAppVisibility);
                        return (IntPtr)1;
                    }

                    if (_freezeHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        if (_isMouseRecording)
                        {
                            Dispatcher.InvokeAsync(StopMouseRecording);
                        }
                        else
                        {
                            Dispatcher.InvokeAsync(ToggleCursorDecoy);
                        }
                        return (IntPtr)1;
                    }

                    if (_mousePlayHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        if (_isMouseRecording)
                        {
                            Dispatcher.InvokeAsync(StopMouseRecording);
                        }
                        else
                        {
                            Dispatcher.InvokeAsync(() => OnToggleMousePlayClick(null!, null!));
                        }
                        return (IntPtr)1;
                    }

                    if (_jigglerHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(ToggleAutoJitter);
                        return (IntPtr)1;
                    }

                    if (_camHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(ToggleFreezeCamera);
                        return (IntPtr)1;
                    }

                    if (_camToggleHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(() => OnActivateCamClick(null!, null!));
                        return (IntPtr)1;
                    }

                    if (_camLagHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(() => OnStutterOptionClick(null!, null!));
                        return (IntPtr)1;
                    }

                    if (_micHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(ToggleMicFakeMute);
                        return (IntPtr)1;
                    }

                    if (_micToggleHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(() => OnActivateMicClick(null!, null!));
                        return (IntPtr)1;
                    }

                    if (_micLagHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(() => OnChoppyOptionClick(null!, null!));
                        return (IntPtr)1;
                    }

                    AppShortcutItem? matched = null;
                    lock (_appShortcutsLock)
                    {
                        matched = _appShortcuts.FirstOrDefault(s => s.Hotkey.IsMatch(ctrl, alt, shift, vk));
                    }
                    if (matched != null)
                    {
                        IntPtr targetHwnd = matched.Handle;
                        Dispatcher.InvokeAsync(() => ToggleAppWindowFocus(targetHwnd));
                        return (IntPtr)1;
                    }
                }
            }
            catch { }
        }
        return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    
    private void ToggleAppVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized && NativeMethods.GetForegroundWindow() == _selfHwnd)
        {
            Hide();
        }
        else
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            ForceForegroundWindow(_selfHwnd);
        }
    }

    
    private void ToggleAppWindowFocus(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd)) return;

        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == hWnd)
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MINIMIZE);
        }
        else
        {
            ForceForegroundWindow(hWnd);
        }
    }

    
    private void UpdateSelfHotkeyVisual()
    {
        if (SelfHotkeyValueText != null)
        {
            SelfHotkeyValueText.Text = _selfHotkey.DisplayText;
            SelfHotkeyValueText.Foreground = _selfHotkey.IsAssigned
                ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
                : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
        }
        if (HeaderHotkeyValueText != null)
        {
            HeaderHotkeyValueText.Text = _selfHotkey.DisplayText;
            HeaderHotkeyValueText.Foreground = _selfHotkey.IsAssigned
                ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
                : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
        }
    }

        private void UpdateFreezeHotkeyVisual() => UpdateMouseHotkeyVisual();

    
    private void UpdateMouseHotkeyVisual()
    {
        if (MouseHotkeyNameText == null || FreezeHotkeyValueText == null) return;
        HotkeyBinding activeBinding;
        switch (_mouseHotkeyIndex)
        {
            case 1:
                MouseHotkeyNameText.Text = "Reproducir clon:";
                activeBinding = _mousePlayHotkey;
                break;
            case 2:
                MouseHotkeyNameText.Text = "Anti-inactividad:";
                activeBinding = _jigglerHotkey;
                break;
            default:
                _mouseHotkeyIndex = 0;
                MouseHotkeyNameText.Text = "Congelar cursor:";
                activeBinding = _freezeHotkey;
                break;
        }

        FreezeHotkeyValueText.Text = activeBinding.DisplayText;
        FreezeHotkeyValueText.Foreground = activeBinding.IsAssigned
            ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
            : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
    }

    
    private void OnMouseHotkeyPrevClick(object sender, RoutedEventArgs e)
    {
        _mouseHotkeyIndex = (_mouseHotkeyIndex - 1 + 3) % 3;
        UpdateMouseHotkeyVisual();
    }

    
    private void OnMouseHotkeyNextClick(object sender, RoutedEventArgs e)
    {
        _mouseHotkeyIndex = (_mouseHotkeyIndex + 1) % 3;
        UpdateMouseHotkeyVisual();
    }

    
    private void OnEditFreezeHotkeyClick(object sender, RoutedEventArgs e) => OnEditCurrentMouseHotkeyClick(sender, e);

    
    private void OnEditCurrentMouseHotkeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string title;
            HotkeyBinding target;
            switch (_mouseHotkeyIndex)
            {
                case 1:
                    title = "Reproducir Clon de Ratón";
                    target = _mousePlayHotkey;
                    break;
                case 2:
                    title = "Simulación Anti-inactividad (Jiggler)";
                    target = _jigglerHotkey;
                    break;
                default:
                    title = "Congelar Cursor (Réplica)";
                    target = _freezeHotkey;
                    break;
            }

            var dlg = new HotkeyCaptureWindow(title, target) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                target.Ctrl = dlg.ResultBinding.Ctrl;
                target.Alt = dlg.ResultBinding.Alt;
                target.Shift = dlg.ResultBinding.Shift;
                target.VirtualKey = dlg.ResultBinding.VirtualKey;
                UpdateMouseHotkeyVisual();
            }
        }
        catch { }
    }

        private void UpdateCamHotkeyVisual()
    {
        if (CamHotkeyNameText == null || CamHotkeyValueText == null) return;
        HotkeyBinding activeBinding;
        switch (_camHotkeyIndex)
        {
            case 1:
                CamHotkeyNameText.Text = "Activar cámara:";
                activeBinding = _camToggleHotkey;
                break;
            case 2:
                CamHotkeyNameText.Text = "Señal inestable:";
                activeBinding = _camLagHotkey;
                break;
            default:
                _camHotkeyIndex = 0;
                CamHotkeyNameText.Text = "Congelar fotograma:";
                activeBinding = _camHotkey;
                break;
        }

        CamHotkeyValueText.Text = activeBinding.DisplayText;
        CamHotkeyValueText.Foreground = activeBinding.IsAssigned
            ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
            : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
    }

    
    private void OnCamHotkeyPrevClick(object sender, RoutedEventArgs e)
    {
        _camHotkeyIndex = (_camHotkeyIndex - 1 + 3) % 3;
        UpdateCamHotkeyVisual();
    }

    
    private void OnCamHotkeyNextClick(object sender, RoutedEventArgs e)
    {
        _camHotkeyIndex = (_camHotkeyIndex + 1) % 3;
        UpdateCamHotkeyVisual();
    }

    
    private void OnEditCamHotkeyClick(object sender, RoutedEventArgs e) => OnEditCurrentCamHotkeyClick(sender, e);

    
    private void OnEditCurrentCamHotkeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string title;
            HotkeyBinding target;
            switch (_camHotkeyIndex)
            {
                case 1:
                    title = "Activar / Apagar Cámara";
                    target = _camToggleHotkey;
                    break;
                case 2:
                    title = "Señal Inestable / Trabado Progresivo";
                    target = _camLagHotkey;
                    break;
                default:
                    title = "Congelar Fotograma de Cámara";
                    target = _camHotkey;
                    break;
            }

            var dlg = new HotkeyCaptureWindow(title, target) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                target.Ctrl = dlg.ResultBinding.Ctrl;
                target.Alt = dlg.ResultBinding.Alt;
                target.Shift = dlg.ResultBinding.Shift;
                target.VirtualKey = dlg.ResultBinding.VirtualKey;
                UpdateCamHotkeyVisual();
            }
        }
        catch { }
    }

        private void UpdateMicHotkeyVisual()
    {
        if (MicHotkeyNameText == null || MicHotkeyValueText == null) return;
        HotkeyBinding activeBinding;
        switch (_micHotkeyIndex)
        {
            case 1:
                MicHotkeyNameText.Text = "Activar micro:";
                activeBinding = _micToggleHotkey;
                break;
            case 2:
                MicHotkeyNameText.Text = "Voz entrecortada:";
                activeBinding = _micLagHotkey;
                break;
            default:
                _micHotkeyIndex = 0;
                MicHotkeyNameText.Text = "Mute falso:";
                activeBinding = _micHotkey;
                break;
        }

        MicHotkeyValueText.Text = activeBinding.DisplayText;
        MicHotkeyValueText.Foreground = activeBinding.IsAssigned
            ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
            : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
    }

    
    private void OnMicHotkeyPrevClick(object sender, RoutedEventArgs e)
    {
        _micHotkeyIndex = (_micHotkeyIndex - 1 + 3) % 3;
        UpdateMicHotkeyVisual();
    }

    
    private void OnMicHotkeyNextClick(object sender, RoutedEventArgs e)
    {
        _micHotkeyIndex = (_micHotkeyIndex + 1) % 3;
        UpdateMicHotkeyVisual();
    }

    
    private void OnEditMicHotkeyClick(object sender, RoutedEventArgs e) => OnEditCurrentMicHotkeyClick(sender, e);

    
    private void OnEditCurrentMicHotkeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string title;
            HotkeyBinding target;
            switch (_micHotkeyIndex)
            {
                case 1:
                    title = "Activar / Desactivar Micrófono";
                    target = _micToggleHotkey;
                    break;
                case 2:
                    title = "Voz Entrecortada (Pérdida de Paquetes)";
                    target = _micLagHotkey;
                    break;
                default:
                    title = "Falso Silencio (Micrófono)";
                    target = _micHotkey;
                    break;
            }

            var dlg = new HotkeyCaptureWindow(title, target) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                target.Ctrl = dlg.ResultBinding.Ctrl;
                target.Alt = dlg.ResultBinding.Alt;
                target.Shift = dlg.ResultBinding.Shift;
                target.VirtualKey = dlg.ResultBinding.VirtualKey;
                UpdateMicHotkeyVisual();
            }
        }
        catch { }
    }

    
private void OnEditSelfHotkeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new HotkeyCaptureWindow("CastDecoy (Abrir/Minimizar)", _selfHotkey) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _selfHotkey.Ctrl = dlg.ResultBinding.Ctrl;
                _selfHotkey.Alt = dlg.ResultBinding.Alt;
                _selfHotkey.Shift = dlg.ResultBinding.Shift;
                _selfHotkey.VirtualKey = dlg.ResultBinding.VirtualKey;
                UpdateSelfHotkeyVisual();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al configurar atajo: {ex.Message}", "CastDecoy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

        private void SyncAppShortcuts()
    {
        lock (_appShortcutsLock)
        {
            var nonSelfWindows = _allWindows.Where(w => !w.IsSelf).ToList();
            var existingMap = _appShortcuts.ToDictionary(s => s.Handle, s => s);

            var toRemove = _appShortcuts.Where(s => !nonSelfWindows.Any(w => w.Handle == s.Handle)).ToList();
            foreach (var r in toRemove) _appShortcuts.Remove(r);

            foreach (var win in nonSelfWindows)
            {
                if (existingMap.TryGetValue(win.Handle, out var existing))
                {
                    existing.Title = win.Title;
                    existing.Icon = win.Icon;
                    if (win.IconBackground != null) existing.IconBackground = win.IconBackground;
                    if (win.IconBorderBrush != null) existing.IconBorderBrush = win.IconBorderBrush;
                }
                else
                {
                    HotkeyBinding initialBinding = new();
                    if (!string.IsNullOrWhiteSpace(win.ProcessName) && _savedAppHotkeys.TryGetValue(win.ProcessName, out var saved))
                    {
                        initialBinding = saved.Clone();
                    }

                    var item = new AppShortcutItem
                    {
                        Handle = win.Handle,
                        ProcessId = win.ProcessId,
                        ProcessName = win.ProcessName,
                        Title = win.Title,
                        Icon = win.Icon,
                        IconBackground = win.IconBackground,
                        IconBorderBrush = win.IconBorderBrush,
                        Hotkey = initialBinding
                    };

                    _appShortcuts.Add(item);
                }
            }
        }

        if (AppShortcutsListBox != null && AppShortcutsListBox.ItemsSource == null)
        {
            AppShortcutsListBox.ItemsSource = _appShortcuts;
        }
    }

    
    private void OnEditAppShortcutClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement elem && elem.DataContext is AppShortcutItem item)
            {
                var dlg = new HotkeyCaptureWindow($"Atajo para {item.ProcessName}", item.Hotkey) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    item.Hotkey.Ctrl = dlg.ResultBinding.Ctrl;
                    item.Hotkey.Alt = dlg.ResultBinding.Alt;
                    item.Hotkey.Shift = dlg.ResultBinding.Shift;
                    item.Hotkey.VirtualKey = dlg.ResultBinding.VirtualKey;
                    item.NotifyHotkeyChanged();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al configurar atajo: {ex.Message}", "CastDecoy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

}
