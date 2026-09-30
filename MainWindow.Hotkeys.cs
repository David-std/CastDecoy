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
    private readonly HotkeyBinding _mouseRecordHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x47 };
    private readonly HotkeyBinding _mousePlayHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x52 };
    private readonly HotkeyBinding _jigglerHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x4A };
    private readonly HotkeyBinding _mouseLagHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x4C };
    private readonly HotkeyBinding _mouseDriftHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x44 };
    private readonly HotkeyBinding _camHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x43 };
    private readonly HotkeyBinding _camToggleHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x56 };
    private readonly HotkeyBinding _camLagHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x53 };
    private readonly HotkeyBinding _camColorHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x42 };
    private readonly HotkeyBinding _camGlitchHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x58 };
    private HotkeyBinding _micHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x4D };
    private readonly HotkeyBinding _micToggleHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x55 };
    private readonly HotkeyBinding _micLagHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x4B };
    private readonly HotkeyBinding _micSatHotkey = new() { Ctrl = true, Shift = true, VirtualKey = 0x45 };
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

                    if (_mouseRecordHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(() => OnToggleMouseRecordClick(null!, null!));
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

                    if (_mouseLagHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(ToggleMouseLag);
                        return (IntPtr)1;
                    }

                    if (_mouseDriftHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(ToggleOrganicDrift);
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

                    if (_camColorHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(() => OnColorOptionClick(null!, null!));
                        return (IntPtr)1;
                    }

                    if (_camGlitchHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(() => OnGlitchOptionClick(null!, null!));
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

                    if (_micSatHotkey.IsMatch(ctrl, alt, shift, vk))
                    {
                        Dispatcher.InvokeAsync(() => OnMicSaturationOptionClick(null!, null!));
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
                MouseHotkeyNameText.Text = "Grabar ruta:";
                activeBinding = _mouseRecordHotkey;
                break;
            case 2:
                MouseHotkeyNameText.Text = "Reproducir clon:";
                activeBinding = _mousePlayHotkey;
                break;
            case 3:
                MouseHotkeyNameText.Text = "Anti-inactividad:";
                activeBinding = _jigglerHotkey;
                break;
            case 4:
                MouseHotkeyNameText.Text = "Latencia y jitter:";
                activeBinding = _mouseLagHotkey;
                break;
            case 5:
                MouseHotkeyNameText.Text = "Deriva orgánica:";
                activeBinding = _mouseDriftHotkey;
                break;
            default:
                _mouseHotkeyIndex = 0;
                MouseHotkeyNameText.Text = "Congelar cursor:";
                activeBinding = _freezeHotkey;
                break;
        }

        if (MouseHotkeyIndexText != null)
        {
            MouseHotkeyIndexText.Text = $"{_mouseHotkeyIndex + 1}/6";
        }

        FreezeHotkeyValueText.Text = activeBinding.DisplayText;
        FreezeHotkeyValueText.Foreground = activeBinding.IsAssigned
            ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
            : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
    }

    private void OnMouseHotkeyPrevClick(object sender, RoutedEventArgs e)
    {
        _mouseHotkeyIndex = (_mouseHotkeyIndex - 1 + 6) % 6;
        UpdateMouseHotkeyVisual();
    }

    private void OnMouseHotkeyNextClick(object sender, RoutedEventArgs e)
    {
        _mouseHotkeyIndex = (_mouseHotkeyIndex + 1) % 6;
        UpdateMouseHotkeyVisual();
    }

    private void OnEditCurrentMouseHotkeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string title;
            HotkeyBinding target;
            switch (_mouseHotkeyIndex)
            {
                case 1:
                    title = "Grabar Ruta de Ratón";
                    target = _mouseRecordHotkey;
                    break;
                case 2:
                    title = "Reproducir Clon de Ratón";
                    target = _mousePlayHotkey;
                    break;
                case 3:
                    title = "Simulación Anti-inactividad (Jiggler)";
                    target = _jigglerHotkey;
                    break;
                case 4:
                    title = "Cursor con Latencia e Inestabilidad";
                    target = _mouseLagHotkey;
                    break;
                case 5:
                    title = "Deriva Orgánica Continua";
                    target = _mouseDriftHotkey;
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
                UpdateAllHotkeysVisuals();
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
            case 3:
                CamHotkeyNameText.Text = "Color de espera:";
                activeBinding = _camColorHotkey;
                break;
            case 4:
                CamHotkeyNameText.Text = "Video cortado:";
                activeBinding = _camGlitchHotkey;
                break;
            default:
                _camHotkeyIndex = 0;
                CamHotkeyNameText.Text = "Congelar fotograma:";
                activeBinding = _camHotkey;
                break;
        }

        if (CamHotkeyIndexText != null)
        {
            CamHotkeyIndexText.Text = $"{_camHotkeyIndex + 1}/5";
        }

        CamHotkeyValueText.Text = activeBinding.DisplayText;
        CamHotkeyValueText.Foreground = activeBinding.IsAssigned
            ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
            : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
    }

    private void OnCamHotkeyPrevClick(object sender, RoutedEventArgs e)
    {
        _camHotkeyIndex = (_camHotkeyIndex - 1 + 5) % 5;
        UpdateCamHotkeyVisual();
    }

    private void OnCamHotkeyNextClick(object sender, RoutedEventArgs e)
    {
        _camHotkeyIndex = (_camHotkeyIndex + 1) % 5;
        UpdateCamHotkeyVisual();
    }

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
                    title = "Señal Inestable / Pérdida";
                    target = _camLagHotkey;
                    break;
                case 3:
                    title = "Color de Espera / Pantalla";
                    target = _camColorHotkey;
                    break;
                case 4:
                    title = "Video Cortado / Fallo de Sincronía";
                    target = _camGlitchHotkey;
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
                UpdateAllHotkeysVisuals();
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
            case 3:
                MicHotkeyNameText.Text = "Saturación extrema:";
                activeBinding = _micSatHotkey;
                break;
            default:
                _micHotkeyIndex = 0;
                MicHotkeyNameText.Text = "Mute falso:";
                activeBinding = _micHotkey;
                break;
        }

        if (MicHotkeyIndexText != null)
        {
            MicHotkeyIndexText.Text = $"{_micHotkeyIndex + 1}/4";
        }

        MicHotkeyValueText.Text = activeBinding.DisplayText;
        MicHotkeyValueText.Foreground = activeBinding.IsAssigned
            ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
            : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
    }

    private void OnMicHotkeyPrevClick(object sender, RoutedEventArgs e)
    {
        _micHotkeyIndex = (_micHotkeyIndex - 1 + 4) % 4;
        UpdateMicHotkeyVisual();
    }

    private void OnMicHotkeyNextClick(object sender, RoutedEventArgs e)
    {
        _micHotkeyIndex = (_micHotkeyIndex + 1) % 4;
        UpdateMicHotkeyVisual();
    }

    private void OnEditCurrentMicHotkeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string title;
            HotkeyBinding target;
            switch (_micHotkeyIndex)
            {
                case 1:
                    title = "Activar / Apagar Micrófono";
                    target = _micToggleHotkey;
                    break;
                case 2:
                    title = "Voz Entrecortada / Pérdida";
                    target = _micLagHotkey;
                    break;
                case 3:
                    title = "Saturación Extrema de Audio";
                    target = _micSatHotkey;
                    break;
                default:
                    title = "Mute Falso de Micrófono";
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
                UpdateAllHotkeysVisuals();
            }
        }
        catch { }
    }

    
    private void EditSpecificHotkey(string title, HotkeyBinding target, Action? postUpdate = null)
    {
        try
        {
            var dlg = new HotkeyCaptureWindow(title, target) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                target.Ctrl = dlg.ResultBinding.Ctrl;
                target.Alt = dlg.ResultBinding.Alt;
                target.Shift = dlg.ResultBinding.Shift;
                target.VirtualKey = dlg.ResultBinding.VirtualKey;
                postUpdate?.Invoke();
                UpdateAllHotkeysVisuals();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al configurar atajo: {ex.Message}", "CastDecoy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnEditSelfHotkeyClick(object sender, RoutedEventArgs e) =>
        EditSpecificHotkey("CastDecoy (Abrir/Minimizar)", _selfHotkey, UpdateSelfHotkeyVisual);

    private void OnEditFreezeHotkeyClick(object sender, RoutedEventArgs e) =>
        EditSpecificHotkey("Congelar Cursor (Réplica)", _freezeHotkey, UpdateMouseHotkeyVisual);

    private void OnEditRecordHotkeyClick(object sender, RoutedEventArgs e) =>
        EditSpecificHotkey("Grabar Ruta de Ratón", _mouseRecordHotkey, UpdateMouseHotkeyVisual);

    private void OnEditPlayHotkeyClick(object sender, RoutedEventArgs e) =>
        EditSpecificHotkey("Reproducir Clon de Ratón", _mousePlayHotkey, UpdateMouseHotkeyVisual);

    private void OnEditJigglerHotkeyClick(object sender, RoutedEventArgs e) =>
        EditSpecificHotkey("Simulación Anti-inactividad (Jiggler)", _jigglerHotkey, UpdateMouseHotkeyVisual);

    private void OnEditCamHotkeyClick(object sender, RoutedEventArgs e) =>
        EditSpecificHotkey("Congelar Fotograma de Cámara", _camHotkey, UpdateCamHotkeyVisual);

    private void OnEditCamToggleHotkeyClick(object sender, RoutedEventArgs e) =>
        EditSpecificHotkey("Activar / Apagar Cámara", _camToggleHotkey, UpdateCamHotkeyVisual);

    private void OnEditCamLagHotkeyClick(object sender, RoutedEventArgs e) =>
        EditSpecificHotkey("Señal Inestable / Pérdida", _camLagHotkey, UpdateCamHotkeyVisual);

    private void OnEditMicHotkeyClick(object sender, RoutedEventArgs e) =>
        EditSpecificHotkey("Mute Falso de Micrófono", _micHotkey, UpdateMicHotkeyVisual);

    private void OnEditMicLagHotkeyClick(object sender, RoutedEventArgs e) =>
        EditSpecificHotkey("Voz Entrecortada / Pérdida", _micLagHotkey, UpdateMicHotkeyVisual);

    private void UpdateAllHotkeysVisuals()
    {
        UpdateSelfHotkeyVisual();
        UpdateMouseHotkeyVisual();
        UpdateCamHotkeyVisual();
        UpdateMicHotkeyVisual();

        if (Tab5FreezeHotkeyValueText != null)
        {
            Tab5FreezeHotkeyValueText.Text = _freezeHotkey.DisplayText;
            Tab5FreezeHotkeyValueText.Foreground = _freezeHotkey.IsAssigned
                ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
                : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
        }
        if (Tab5RecordHotkeyValueText != null)
        {
            Tab5RecordHotkeyValueText.Text = _mouseRecordHotkey.DisplayText;
            Tab5RecordHotkeyValueText.Foreground = _mouseRecordHotkey.IsAssigned
                ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
                : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
        }
        if (Tab5PlayHotkeyValueText != null)
        {
            Tab5PlayHotkeyValueText.Text = _mousePlayHotkey.DisplayText;
            Tab5PlayHotkeyValueText.Foreground = _mousePlayHotkey.IsAssigned
                ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
                : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
        }
        if (Tab5JigglerHotkeyValueText != null)
        {
            Tab5JigglerHotkeyValueText.Text = _jigglerHotkey.DisplayText;
            Tab5JigglerHotkeyValueText.Foreground = _jigglerHotkey.IsAssigned
                ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
                : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
        }
        if (Tab5CamHotkeyValueText != null)
        {
            Tab5CamHotkeyValueText.Text = _camHotkey.DisplayText;
            Tab5CamHotkeyValueText.Foreground = _camHotkey.IsAssigned
                ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
                : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
        }
        if (Tab5CamToggleHotkeyValueText != null)
        {
            Tab5CamToggleHotkeyValueText.Text = _camToggleHotkey.DisplayText;
            Tab5CamToggleHotkeyValueText.Foreground = _camToggleHotkey.IsAssigned
                ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
                : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
        }
        if (Tab5CamLagHotkeyValueText != null)
        {
            Tab5CamLagHotkeyValueText.Text = _camLagHotkey.DisplayText;
            Tab5CamLagHotkeyValueText.Foreground = _camLagHotkey.IsAssigned
                ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
                : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
        }
        if (Tab5MicHotkeyValueText != null)
        {
            Tab5MicHotkeyValueText.Text = _micHotkey.DisplayText;
            Tab5MicHotkeyValueText.Foreground = _micHotkey.IsAssigned
                ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
                : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
        }
        if (Tab5MicLagHotkeyValueText != null)
        {
            Tab5MicLagHotkeyValueText.Text = _micLagHotkey.DisplayText;
            Tab5MicLagHotkeyValueText.Foreground = _micLagHotkey.IsAssigned
                ? new SolidColorBrush(MediaColor.FromRgb(0x1d, 0x1d, 0x1f))
                : new SolidColorBrush(MediaColor.FromRgb(0x8e, 0x8e, 0x93));
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
