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
    private Window? _decoyCursorWindow;
    private Window? _followerCursorWindow;
    private IntPtr _followerHwnd = IntPtr.Zero;
    private Window? _jitterCursorWindow;
    private IntPtr _jitterHwnd = IntPtr.Zero;
    private NativeMethods.LowLevelProc? _mouseHookProc;
    private IntPtr _mouseHookHandle = IntPtr.Zero;
    private DispatcherTimer? _followerTimer;
    private bool _isDecoyActive = false;

    
    public struct MousePoint
    {
        public int X { get; set; }
        public int Y { get; set; }
        public long ElapsedMs { get; set; }
        public bool IsClick { get; set; }
        public MousePoint(int x, int y, long elapsedMs, bool isClick = false)
        {
            X = x;
            Y = y;
            ElapsedMs = elapsedMs;
            IsClick = isClick;
        }
    }

    private int _recLeftClicks = 0;
    private int _recRightClicks = 0;
    private bool _prevLButtonDown = false;
    private bool _prevRButtonDown = false;
    private readonly List<MousePoint> _recordedMouseTrack = new();
    private readonly object _mouseTrackLock = new();
    private bool _isMouseRecording = false;
    private bool _isMousePlaying = false;
    private readonly Stopwatch _mouseRecordStopwatch = new();
    private DispatcherTimer? _mouseRecordTimer;
    private readonly Stopwatch _mousePlayStopwatch = new();
    private DispatcherTimer? _mousePlayTimer;
    private Window? _cloneCursorWindow;
    private IntPtr _cloneHwnd = IntPtr.Zero;
    private double _mousePlaySpeed = 1.0;
    private bool _isMouseLagActive = false;
    private int _mouseLagMs = 250;
    private bool _showLagGhost = true;
    private Window? _lagCursorWindow;
    private IntPtr _lagHwnd = IntPtr.Zero;
    private DispatcherTimer? _mouseLagTimer;
    private readonly Stopwatch _mouseLagStopwatch = new();
    private readonly List<(int X, int Y, long TimeMs)> _mouseLagHistory = new();
    private int _mouseLagDroppedFrames = 0;
    private int _currentLagTargetX = 0;
    private int _currentLagTargetY = 0;

    private bool _isOrganicDriftActive = false;
    private string _driftPattern = "Oscilación biológica";
    private Window? _driftCursorWindow;
    private IntPtr _driftHwnd = IntPtr.Zero;
    private DispatcherTimer? _driftTimer;
    private readonly Stopwatch _driftStopwatch = new();
    private int _driftBaseX = 0;
    private int _driftBaseY = 0;
    private int _currentDriftX = 0;
    private int _currentDriftY = 0;

    private enum ShapeToolMode { None, Freehand, Line, Rect, Circle }
    private ShapeToolMode _currentShapeTool = ShapeToolMode.None;
    private bool _isDrawingFreehand = false;
    private bool _isDraggingShapeBody = false;
    private int _draggedHandleIndex = 0;
    private System.Windows.Point _lastMouseCanvasPos;
    private System.Windows.Point _lineStart = new(35, 82);
    private System.Windows.Point _lineEnd = new(289, 82);
    private Rect _rectBounds = new(40, 25, 244, 115);
    private System.Windows.Point _circleCenter = new(162, 82);
    private double _circleRadius = 55;
    private bool _isAutoJitterActive = false;
    private DispatcherTimer? _autoJitterTimer;
    private int _autoJitterIntervalSeconds = 30;

    private void StartFollowerCursor()
    {
        if (_followerCursorWindow == null)
        {
            _followerCursorWindow = CreateCursorWindow(excludeFromCapture: true);
        }
        _followerCursorWindow.Show();
        _followerHwnd = new WindowInteropHelper(_followerCursorWindow).EnsureHandle();

        NativeMethods.GetCursorPos(out var pt);
        NativeMethods.SetWindowPos(_followerHwnd, NativeMethods.HWND_TOPMOST, pt.X, pt.Y, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

        if (_mouseHookHandle == IntPtr.Zero)
        {
            try
            {
                _mouseHookProc = MouseHookCallback;
                IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
                _mouseHookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookProc, moduleHandle, 0);
            }
            catch { }
        }

        if (_followerTimer == null)
        {
            _followerTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(10)
            };
            _followerTimer.Tick += (_, _) =>
            {
                if (_followerHwnd != IntPtr.Zero && (_isDecoyActive || _isMousePlaying || _isMouseLagActive || _isAutoJitterActive || _isOrganicDriftActive))
                {
                    NativeMethods.GetCursorPos(out var curPt);
                    NativeMethods.SetWindowPos(_followerHwnd, NativeMethods.HWND_TOPMOST, curPt.X, curPt.Y, 0, 0,
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE);
                }
            };
        }
        _followerTimer.Start();
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_MOUSEMOVE)
        {
            try
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if (_followerHwnd != IntPtr.Zero && (_isDecoyActive || _isMousePlaying || _isMouseLagActive || _isAutoJitterActive || _isOrganicDriftActive))
                {
                    NativeMethods.SetWindowPos(_followerHwnd, NativeMethods.HWND_TOPMOST, hookStruct.pt.X, hookStruct.pt.Y, 0, 0,
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE);
                }
            }
            catch { }
        }
        return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void RestoreRealCursor()
    {
        try
        {
            NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETCURSORS, 0, IntPtr.Zero, 0);
        }
        catch { }

        if (_mouseHookHandle != IntPtr.Zero)
        {
            try { NativeMethods.UnhookWindowsHookEx(_mouseHookHandle); } catch { }
            _mouseHookHandle = IntPtr.Zero;
        }

        _followerTimer?.Stop();
        _decoyCursorWindow?.Hide();
        _followerCursorWindow?.Hide();
        _cloneCursorWindow?.Hide();
        _lagCursorWindow?.Hide();
        _jitterCursorWindow?.Hide();
        _driftCursorWindow?.Hide();
    }

    private void StopAllMouseDecoys(bool keepFollower = false)
    {
        _isDecoyActive = false;
        _decoyCursorWindow?.Hide();

        _isMousePlaying = false;
        _mousePlayTimer?.Stop();
        _mousePlayStopwatch.Stop();
        _cloneCursorWindow?.Hide();

        _isMouseLagActive = false;
        _mouseLagTimer?.Stop();
        _mouseLagStopwatch.Stop();
        _lagCursorWindow?.Hide();

        _isAutoJitterActive = false;
        _autoJitterTimer?.Stop();
        _jitterCursorWindow?.Hide();

        _isOrganicDriftActive = false;
        _driftTimer?.Stop();
        _driftStopwatch.Stop();
        _driftCursorWindow?.Hide();

        if (DecoyToggleBtn != null)
        {
            DecoyToggleBtn.Content = "Congelar";
            DecoyToggleBtn.Style = (Style)FindResource("SecondaryPillBtn");
            DecoyStatusText.Text = "Modo: Pausa de cursor";
            DecoyStatusText.Foreground = InactiveTextBrush;
        }
        if (MousePlayBtn != null)
        {
            MousePlayBtn.Content = "Reproducir";
            MousePlayBtn.Style = (Style)FindResource("SecondaryPillBtn");
            MouseRecordStatusText.Text = "Listo para reproducir";
        }
        if (AutoJitterToggleBtn != null)
        {
            AutoJitterToggleBtn.Content = "Iniciar";
            AutoJitterToggleBtn.Style = (Style)FindResource("SecondaryPillBtn");
            AutoJitterStatusText.Text = "Sin reposo";
            AutoJitterStatusText.Foreground = InactiveTextBrush;
        }
        if (MouseLagToggleBtn != null)
        {
            MouseLagToggleBtn.Content = "Iniciar";
            MouseLagToggleBtn.Style = (Style)FindResource("SecondaryPillBtn");
            MouseLagStatusText.Text = "Sin retraso";
            MouseLagStatusText.Foreground = InactiveTextBrush;
        }
        if (OrganicDriftToggleBtn != null)
        {
            OrganicDriftToggleBtn.Content = "Iniciar";
            OrganicDriftToggleBtn.Style = (Style)FindResource("SecondaryPillBtn");
            OrganicDriftStatusText.Text = "Deriva inactiva";
            OrganicDriftStatusText.Foreground = InactiveTextBrush;
        }

        UpdateMouseLagVisuals();

        if (!keepFollower)
        {
            RestoreRealCursor();
        }
    }

    private void SetMouseLiveMode()
    {
        StopAllMouseDecoys(keepFollower: false);
        if (ModeMouseLiveRadio != null) ModeMouseLiveRadio.IsChecked = true;
        if (TileFreezeStatusText != null)
        {
            TileFreezeStatusText.Text = "Réplica inactiva";
            TileFreezeStatusText.Foreground = InactiveTextBrush;
        }
        if (MouseClonePosText != null) MouseClonePosText.Text = "En reposo";
    }

    private void SetMouseFreezeMode()
    {
        StopAllMouseDecoys(keepFollower: true);
        if (ModeMouseFreezeRadio != null) ModeMouseFreezeRadio.IsChecked = true;

        NativeMethods.GetCursorPos(out var pt);
        if (_decoyCursorWindow == null)
        {
            _decoyCursorWindow = CreateCursorWindow(false);
        }
        _decoyCursorWindow.Show();
        var decoyHwnd = new WindowInteropHelper(_decoyCursorWindow).EnsureHandle();
        NativeMethods.SetWindowPos(decoyHwnd, NativeMethods.HWND_TOPMOST, pt.X, pt.Y, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

        _isDecoyActive = true;
        HideSystemCursor();
        StartFollowerCursor();

        DecoyStatusText.Text = $"Réplica fija en ({pt.X}, {pt.Y}) visible en captura.";
        DecoyStatusText.Foreground = AccentBlueBrush;
        DecoyToggleBtn.Content = "Desactivar";
        DecoyToggleBtn.Style = (Style)FindResource("PrimaryPillBtn");
        TileFreezeStatusText.Text = "Réplica activa";
        TileFreezeStatusText.Foreground = AccentBlueBrush;
    }

    private void SetMouseTrackMode()
    {
        StopAllMouseDecoys(keepFollower: true);
        if (ModeMouseTrackRadio != null) ModeMouseTrackRadio.IsChecked = true;

        lock (_mouseTrackLock)
        {
            if (_recordedMouseTrack.Count < 2)
            {
                LoadPresetMouseMovement(1);
            }
        }

        _isMousePlaying = true;
        _mousePlayStopwatch.Restart();

        if (_cloneCursorWindow == null)
        {
            _cloneCursorWindow = CreateCursorWindow(false);
        }
        _cloneCursorWindow.Show();
        _cloneHwnd = new WindowInteropHelper(_cloneCursorWindow).EnsureHandle();

        HideSystemCursor();
        StartFollowerCursor();

        long totalDuration;
        lock (_mouseTrackLock)
        {
            totalDuration = _recordedMouseTrack[^1].ElapsedMs;
            if (totalDuration <= 0) totalDuration = 1000;
        }

        _mousePlayTimer?.Stop();
        _mousePlayTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _mousePlayTimer.Tick += (_, _) =>
        {
            if (!_isMousePlaying) return;

            double speed = _mousePlaySpeed > 0 ? _mousePlaySpeed : 1.0;
            long elapsed = (long)(_mousePlayStopwatch.ElapsedMilliseconds * speed) % totalDuration;
            MousePoint currentPt = GetInterpolatedPoint(elapsed);

            if (_cloneHwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(_cloneHwnd, NativeMethods.HWND_TOPMOST,
                    currentPt.X, currentPt.Y, 0, 0,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            }

            if (MouseClonePosText != null) MouseClonePosText.Text = $"({currentPt.X}, {currentPt.Y})";
            UpdateMousepadVisual(currentPt.X, currentPt.Y, currentPt.IsClick);
        };
        _mousePlayTimer.Start();

        MousePlayBtn.Content = "Detener clon";
        MousePlayBtn.Style = (Style)FindResource("PrimaryPillBtn");
        MouseRecordStatusText.Text = "Clon activo en bucle";
    }

    private void SetMouseJitterMode()
    {
        StopAllMouseDecoys(keepFollower: true);
        if (ModeMouseJitterRadio != null) ModeMouseJitterRadio.IsChecked = true;

        _isAutoJitterActive = true;
        NativeMethods.GetCursorPos(out var pt);

        if (_jitterCursorWindow == null)
        {
            _jitterCursorWindow = CreateCursorWindow(false);
        }
        _jitterCursorWindow.Show();
        _jitterHwnd = new WindowInteropHelper(_jitterCursorWindow).EnsureHandle();
        NativeMethods.SetWindowPos(_jitterHwnd, NativeMethods.HWND_TOPMOST, pt.X, pt.Y, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

        HideSystemCursor();
        StartFollowerCursor();

        int pulseX = pt.X;
        int pulseY = pt.Y;
        int tickCount = 0;

        _autoJitterTimer?.Stop();
        _autoJitterTimer = new DispatcherTimer();
        _autoJitterTimer.Tick += (_, _) =>
        {
            if (!_isAutoJitterActive) return;
            try
            {
                tickCount++;
                int shift = (tickCount % 2 == 1) ? 2 : -2;
                pulseX += shift;
                if (_jitterHwnd != IntPtr.Zero)
                {
                    NativeMethods.SetWindowPos(_jitterHwnd, NativeMethods.HWND_TOPMOST, pulseX, pulseY, 0, 0,
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE);
                }
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MOVE, 1, 0, 0, UIntPtr.Zero);
                Thread.Sleep(10);
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MOVE, unchecked((uint)-1), 0, 0, UIntPtr.Zero);

                AutoJitterStatusText.Text = $"Activo: Pulso emitido ({DateTime.Now:HH:mm:ss})";
                AutoJitterStatusText.Foreground = AccentBlueBrush;
                UpdateMousepadVisual(pulseX, pulseY, true);
            }
            catch { }
        };
        _autoJitterTimer.Interval = TimeSpan.FromSeconds(_autoJitterIntervalSeconds);
        _autoJitterTimer.Start();

        AutoJitterToggleBtn.Content = "Detener";
        AutoJitterToggleBtn.Style = (Style)FindResource("PrimaryPillBtn");
        AutoJitterStatusText.Text = $"Activo: Pulso cada {_autoJitterIntervalSeconds} s";
        AutoJitterStatusText.Foreground = AccentBlueBrush;
    }

    private void SetMouseLagMode()
    {
        StopAllMouseDecoys(keepFollower: true);
        if (ModeMouseLagRadio != null) ModeMouseLagRadio.IsChecked = true;

        _isMouseLagActive = true;
        if (_lagCursorWindow == null)
        {
            _lagCursorWindow = CreateCursorWindow(false);
        }
        _lagCursorWindow.Show();
        _lagHwnd = new WindowInteropHelper(_lagCursorWindow).EnsureHandle();

        HideSystemCursor();
        StartFollowerCursor();

        lock (_mouseLagHistory)
        {
            _mouseLagHistory.Clear();
        }
        _mouseLagStopwatch.Restart();
        _mouseLagDroppedFrames = 0;

        NativeMethods.GetCursorPos(out var curPt);
        _currentLagTargetX = curPt.X;
        _currentLagTargetY = curPt.Y;

        _mouseLagTimer?.Stop();
        _mouseLagTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _mouseLagTimer.Tick += (_, _) =>
        {
            if (!_isMouseLagActive) return;
            NativeMethods.GetCursorPos(out var realPt);
            long now = _mouseLagStopwatch.ElapsedMilliseconds;

            lock (_mouseLagHistory)
            {
                _mouseLagHistory.Add((realPt.X, realPt.Y, now));
                while (_mouseLagHistory.Count > 0 && (now - _mouseLagHistory[0].TimeMs) > 2000)
                {
                    _mouseLagHistory.RemoveAt(0);
                }
            }

            long targetTime = now - _mouseLagMs;
            int targetX = realPt.X;
            int targetY = realPt.Y;

            lock (_mouseLagHistory)
            {
                if (_mouseLagHistory.Count > 0)
                {
                    var match = _mouseLagHistory.LastOrDefault(p => p.TimeMs <= targetTime);
                    if (match.TimeMs != 0)
                    {
                        targetX = match.X;
                        targetY = match.Y;
                    }
                    else
                    {
                        targetX = _mouseLagHistory[0].X;
                        targetY = _mouseLagHistory[0].Y;
                    }
                }
            }

            if (_mouseLagMs == 350)
            {
                _mouseLagDroppedFrames++;
                if (_mouseLagDroppedFrames % 6 < 3)
                {
                    targetX = _currentLagTargetX;
                    targetY = _currentLagTargetY;
                }
                else
                {
                    _currentLagTargetX = targetX;
                    _currentLagTargetY = targetY;
                }
            }
            else
            {
                _currentLagTargetX = targetX;
                _currentLagTargetY = targetY;
            }

            if (_lagCursorWindow != null && _lagHwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(_lagHwnd, NativeMethods.HWND_TOPMOST,
                    _currentLagTargetX, _currentLagTargetY, 0, 0,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            }

            UpdateMousepadVisual(realPt.X, realPt.Y, false);
        };
        _mouseLagTimer.Start();

        MouseLagToggleBtn.Content = "Detener réplica";
        MouseLagToggleBtn.Style = (Style)FindResource("PrimaryPillBtn");
        MouseLagStatusText.Text = $"Réplica activa ({_mouseLagMs} ms lag)";
        MouseLagStatusText.Foreground = AccentBlueBrush;
        UpdateMouseLagVisuals();
    }

    private void SetMouseDriftMode()
    {
        StopAllMouseDecoys(keepFollower: true);
        if (ModeMouseDriftRadio != null) ModeMouseDriftRadio.IsChecked = true;

        _isOrganicDriftActive = true;
        NativeMethods.GetCursorPos(out var pt);
        _driftBaseX = pt.X;
        _driftBaseY = pt.Y;
        _currentDriftX = pt.X;
        _currentDriftY = pt.Y;

        if (_driftCursorWindow == null)
        {
            _driftCursorWindow = CreateCursorWindow(false);
        }
        _driftCursorWindow.Show();
        _driftHwnd = new WindowInteropHelper(_driftCursorWindow).EnsureHandle();

        HideSystemCursor();
        StartFollowerCursor();

        _driftStopwatch.Restart();
        _driftTimer?.Stop();
        _driftTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _driftTimer.Tick += (_, _) =>
        {
            if (!_isOrganicDriftActive) return;

            double sec = _driftStopwatch.Elapsed.TotalSeconds;
            int screenW = NativeMethods.GetSystemMetrics(0);
            int screenH = NativeMethods.GetSystemMetrics(1);
            if (screenW <= 0) screenW = 1920;
            if (screenH <= 0) screenH = 1080;

            double dx = 0;
            double dy = 0;

            if (_driftPattern == "Oscilación biológica")
            {
                dx = Math.Sin(sec * 1.6) * 45 + Math.Sin(sec * 3.4) * 18 + Math.Cos(sec * 0.7) * 25;
                dy = Math.Cos(sec * 1.2) * 35 + Math.Sin(sec * 2.7) * 14 + Math.Sin(sec * 0.5) * 20;
            }
            else if (_driftPattern == "Círculos sutiles")
            {
                dx = Math.Cos(sec * 0.9) * 60;
                dy = Math.Sin(sec * 0.9) * 60;
            }
            else if (_driftPattern == "Lectura diagonal")
            {
                double progress = (sec % 5.0) / 5.0;
                dx = (progress * 320) - 160;
                dy = (progress * 90) - 45;
            }
            else
            {
                double tClamped = Math.Min(1.0, sec / 6.0);
                double ease = (1 - Math.Cos(tClamped * Math.PI)) / 2;
                double destX = screenW - 120;
                double destY = screenH - 120;
                dx = (_driftBaseX + (destX - _driftBaseX) * ease) - _driftBaseX;
                dy = (_driftBaseY + (destY - _driftBaseY) * ease) - _driftBaseY;
            }

            int finalX = (int)Math.Clamp(_driftBaseX + dx, 10, screenW - 20);
            int finalY = (int)Math.Clamp(_driftBaseY + dy, 10, screenH - 20);
            _currentDriftX = finalX;
            _currentDriftY = finalY;

            if (_driftCursorWindow != null && _driftHwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(_driftHwnd, NativeMethods.HWND_TOPMOST,
                    finalX, finalY, 0, 0,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            }

            UpdateMousepadVisual(finalX, finalY, false);
        };
        _driftTimer.Start();

        OrganicDriftToggleBtn.Content = "Detener deriva";
        OrganicDriftToggleBtn.Style = (Style)FindResource("PrimaryPillBtn");
        OrganicDriftStatusText.Text = $"Deriva activa ({_driftPattern})";
        OrganicDriftStatusText.Foreground = AccentBlueBrush;
    }

    private void ToggleCursorDecoy()
    {
        if (_isDecoyActive) SetMouseLiveMode();
        else SetMouseFreezeMode();
    }

    private void OnToggleFreezeDecoyClick(object sender, RoutedEventArgs e)
    {
        ToggleCursorDecoy();
    }

    private void OnMouseModeRadioClicked(object sender, RoutedEventArgs e)
    {
        if (sender == ModeMouseLiveRadio) SetMouseLiveMode();
        else if (sender == ModeMouseFreezeRadio) SetMouseFreezeMode();
        else if (sender == ModeMouseTrackRadio) SetMouseTrackMode();
        else if (sender == ModeMouseJitterRadio) SetMouseJitterMode();
        else if (sender == ModeMouseLagRadio) SetMouseLagMode();
        else if (sender == ModeMouseDriftRadio) SetMouseDriftMode();
    }

    private void OnMouseLiveOptionClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SetMouseLiveMode();
    }

    private void OnMouseFreezeOptionClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SetMouseFreezeMode();
    }

    private void OnMouseTrackOptionClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SetMouseTrackMode();
    }

    private void OnMouseJitterOptionClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SetMouseJitterMode();
    }

    private void OnMouseLagOptionClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SetMouseLagMode();
    }

    private void OnMouseDriftOptionClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SetMouseDriftMode();
    }

    
    
    private void OnToggleMouseRecordClick(object sender, RoutedEventArgs e)
    {
        if (_isMousePlaying) StopMousePlayback();
        if (_isMouseRecording) StopMouseRecording();
        else StartMouseRecording();
    }

    
    private void StartMouseRecording()
    {
        lock (_mouseTrackLock) { _recordedMouseTrack.Clear(); }
        MousepadTrackLine.Points.Clear();
        DrawingPolyline.Points.Clear();
        HideShapeOverlays();
        _currentShapeTool = ShapeToolMode.None;
        UpdateDrawingToolButtons(null);
        if (DrawingCanvas != null) DrawingCanvas.Cursor = Cursors.Arrow;

        _recLeftClicks = 0;
        _recRightClicks = 0;
        _prevLButtonDown = false;
        _prevRButtonDown = false;
        MouseClicksText.Text = "0 izq • 0 der";
        _isMouseRecording = true;
        _mouseRecordStopwatch.Restart();

        MouseRecordBtn.Content = "Detener grabación";
        MouseRecordBtn.Style = (Style)FindResource("PrimaryPillBtn");
        MousePlayBtn.IsEnabled = false;
        MouseRecordStatusDot.Fill = StatusRedBrush;
        MouseRecordStatusText.Text = "Grabando movimientos en tiempo real...";

        _mouseRecordTimer?.Stop();
        _mouseRecordTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _mouseRecordTimer.Tick += (_, _) =>
        {
            if (!_isMouseRecording) return;
            NativeMethods.GetCursorPos(out var pt);
            if (_isOrganicDriftActive && _currentDriftX > 0 && _currentDriftY > 0)
            {
                pt.X = _currentDriftX;
                pt.Y = _currentDriftY;
            }
            long ms = _mouseRecordStopwatch.ElapsedMilliseconds;

            bool isClick = false;
            if (RecordClicksCheck.IsChecked == true)
            {
                bool lDown = (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0;
                bool rDown = (NativeMethods.GetAsyncKeyState(0x02) & 0x8000) != 0;
                if (lDown && !_prevLButtonDown)
                {
                    isClick = true;
                    _recLeftClicks++;
                }
                if (rDown && !_prevRButtonDown)
                {
                    isClick = true;
                    _recRightClicks++;
                }
                _prevLButtonDown = lDown;
                _prevRButtonDown = rDown;
                if (isClick)
                {
                    MouseClicksText.Text = $"{_recLeftClicks} izq • {_recRightClicks} der";
                }
            }

            lock (_mouseTrackLock)
            {
                if (_recordedMouseTrack.Count == 0 ||
                    _recordedMouseTrack[^1].X != pt.X ||
                    _recordedMouseTrack[^1].Y != pt.Y ||
                    isClick ||
                    (ms - _recordedMouseTrack[^1].ElapsedMs) >= 80)
                {
                    _recordedMouseTrack.Add(new MousePoint(pt.X, pt.Y, ms, isClick));
                }
            }

            MousePointsText.Text = $"{_recordedMouseTrack.Count} pts ({_mouseRecordStopwatch.Elapsed.TotalSeconds:F1}s)";
            MouseDurationText.Text = $"{_mouseRecordStopwatch.Elapsed.TotalSeconds:F1} s";
            UpdateMousepadVisual(pt.X, pt.Y, false);
        };
        _mouseRecordTimer.Start();
    }

    
    private void StopMouseRecording()
    {
        _isMouseRecording = false;
        _mouseRecordTimer?.Stop();
        _mouseRecordStopwatch.Stop();

        MouseRecordBtn.Content = "Iniciar grabación";
        MouseRecordBtn.Style = (Style)FindResource("PrimaryPillBtn");

        int count = 0;
        long durationMs = 0;
        lock (_mouseTrackLock)
        {
            count = _recordedMouseTrack.Count;
            if (count > 0) durationMs = _recordedMouseTrack[^1].ElapsedMs;
        }

        if (count > 1)
        {
            MousePlayBtn.IsEnabled = true;
            MouseRecordStatusDot.Fill = AccentBlueBrush;
            MouseRecordStatusText.Text = $"Grabación lista ({count} puntos, {durationMs / 1000.0:F1}s)";
        }
        else
        {
            MousePlayBtn.IsEnabled = false;
            MouseRecordStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0x5f, 0x63, 0x68));
            MouseRecordStatusText.Text = "Grabación cancelada (muy pocos puntos)";
        }
    }

    
    private void OnToggleMousePlayClick(object sender, RoutedEventArgs e)
    {
        if (_isMouseRecording) StopMouseRecording();
        if (_isMousePlaying) SetMouseLiveMode();
        else SetMouseTrackMode();
    }

    private void StartMousePlayback()
    {
        SetMouseTrackMode();
    }

    private void StopMousePlayback()
    {
        SetMouseLiveMode();
    }

    
    private void HideSystemCursor()
    {
        try
        {
            uint[] cursorIds = {
                NativeMethods.OCR_NORMAL,
                NativeMethods.OCR_IBEAM,
                NativeMethods.OCR_HAND,
                NativeMethods.OCR_WAIT,
                NativeMethods.OCR_CROSS,
                NativeMethods.OCR_SIZEALL,
                NativeMethods.OCR_SIZENWSE,
                NativeMethods.OCR_SIZENESW,
                NativeMethods.OCR_SIZEWE,
                NativeMethods.OCR_SIZENS,
                NativeMethods.OCR_UP
            };
            foreach (var id in cursorIds)
            {
                byte[] andPlane = new byte[128];
                for (int i = 0; i < andPlane.Length; i++) andPlane[i] = 0xFF;
                byte[] xorPlane = new byte[128];
                IntPtr blank = NativeMethods.CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andPlane, xorPlane);
                if (blank != IntPtr.Zero)
                {
                    NativeMethods.SetSystemCursor(blank, id);
                }
            }
        }
        catch { }
    }

    
    private void OnPresetReadingClick(object sender, RoutedEventArgs e)
    {
        if (_isMousePlaying) StopMousePlayback();
        if (_isMouseRecording) StopMouseRecording();
        LoadPresetMouseMovement(1);
    }

    
    private void OnPresetOfficeClick(object sender, RoutedEventArgs e)
    {
        if (_isMousePlaying) StopMousePlayback();
        if (_isMouseRecording) StopMouseRecording();
        LoadPresetMouseMovement(2);
    }

    
    private void OnClearMouseTrackClick(object sender, RoutedEventArgs e)
    {
        if (_isMousePlaying) StopMousePlayback();
        if (_isMouseRecording) StopMouseRecording();

        lock (_mouseTrackLock)
        {
            _recordedMouseTrack.Clear();
        }

        DrawingPolyline.Points.Clear();
        MousepadTrackLine.Points.Clear();
        HideShapeOverlays();
        _currentShapeTool = ShapeToolMode.None;
        UpdateDrawingToolButtons(null);
        if (DrawingCanvas != null) DrawingCanvas.Cursor = Cursors.Arrow;

        _recLeftClicks = 0;
        _recRightClicks = 0;
        MousePointsText.Text = "0 pts • 0.0s";
        MouseClicksText.Text = "0 izq • 0 der";
        MouseDurationText.Text = "0.0 s";
        MouseClonePosText.Text = "En reposo";
        MousePlayBtn.IsEnabled = false;
        MouseRecordStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0x5f, 0x63, 0x68));
        MouseRecordStatusText.Text = "Ruta y pizarra borradas. Listo para grabar o dibujar.";
        CenterMousepadDot();
    }

        private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scv)
        {
            scv.ScrollToVerticalOffset(scv.VerticalOffset - (e.Delta / 2.0));
            e.Handled = true;
        }
    }

    
    private void OnToggleMouseLagClick(object sender, RoutedEventArgs e)
    {
        ToggleMouseLag();
    }

    public void ToggleMouseLag()
    {
        if (_isMouseLagActive)
        {
            SetMouseLiveMode();
        }
        else
        {
            SetMouseLagMode();
        }
    }

    private void StartMouseLag()
    {
        SetMouseLagMode();
    }

    private void StopMouseLag()
    {
        SetMouseLiveMode();
    }

    private void OnSimulateMouseLagCheckClicked(object sender, RoutedEventArgs e)
    {
        ToggleMouseLag();
    }

    private void OnMouseLagChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MouseLagCombo == null) return;
        int idx = MouseLagCombo.SelectedIndex;
        if (idx == 0) _mouseLagMs = 100;
        else if (idx == 1) _mouseLagMs = 250;
        else if (idx == 2) _mouseLagMs = 500;
        else if (idx == 3) _mouseLagMs = 350;

        if (_isMouseLagActive)
        {
            MouseLagStatusText.Text = $"Réplica activa ({_mouseLagMs} ms lag)";
        }
    }

    private void OnShowLagGhostCheckClicked(object sender, RoutedEventArgs e)
    {
        _showLagGhost = ShowLagGhostCheck?.IsChecked == true;
        if (!_showLagGhost)
        {
            _lagCursorWindow?.Hide();
        }
        else if (_isMouseLagActive && _lagCursorWindow != null)
        {
            _lagCursorWindow.Show();
        }
        UpdateMouseLagVisuals();
    }

    private void UpdateMouseLagVisuals()
    {
        if (MousepadLagDot != null)
        {
            MousepadLagDot.Visibility = (_isMouseLagActive && _showLagGhost) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    
    private void OnMouseSpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MouseSpeedCombo?.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Content?.ToString()?.Replace("x", "").Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double spd))
        {
            _mousePlaySpeed = spd;
        }
    }

    
    private void LoadPresetMouseMovement(int presetId)
    {
        lock (_mouseTrackLock)
        {
            _recordedMouseTrack.Clear();
            int screenW = WinForms.Screen.PrimaryScreen?.Bounds.Width ?? 1920;
            int screenH = WinForms.Screen.PrimaryScreen?.Bounds.Height ?? 1080;
            int startX = screenW / 3;
            int startY = screenH / 3;

            if (presetId == 1)
            {
                long t = 0;
                for (int line = 0; line < 5; line++)
                {
                    int lineY = startY + line * 36;
                    for (int x = startX; x <= startX + 520; x += 18)
                    {
                        _recordedMouseTrack.Add(new MousePoint(x, lineY + (int)(Math.Sin(x * 0.04) * 3), t));
                        t += 35;
                    }
                    t += 350;
                    _recordedMouseTrack.Add(new MousePoint(startX, lineY + 36, t));
                    t += 200;
                }
            }
            else
            {
                long t = 0;
                var waypoints = new (int X, int Y, int delay)[]
                {
                    (startX + 120, 70, 600),
                    (startX + 280, 70, 500),
                    (startX + 160, 130, 700),
                    (startX + 220, screenH / 2, 1100),
                    (startX + 420, screenH / 2 + 80, 700),
                    (screenW - 140, screenH / 2, 800),
                    (startX, startY, 600)
                };
                int curX = startX, curY = startY;
                foreach (var wp in waypoints)
                {
                    int steps = 25;
                    for (int s = 1; s <= steps; s++)
                    {
                        double progress = (double)s / steps;
                        double ease = (1 - Math.Cos(progress * Math.PI)) / 2;
                        int nx = (int)(curX + (wp.X - curX) * ease);
                        int ny = (int)(curY + (wp.Y - curY) * ease);
                        _recordedMouseTrack.Add(new MousePoint(nx, ny, t));
                        t += 25;
                    }
                    curX = wp.X;
                    curY = wp.Y;
                    t += wp.delay;
                }
            }
        }

        MousePointsText.Text = $"{_recordedMouseTrack.Count} pts";
        MouseDurationText.Text = $"{_recordedMouseTrack[^1].ElapsedMs / 1000.0:F1} s";
        MousePlayBtn.IsEnabled = true;
        MouseRecordStatusDot.Fill = AccentBlueBrush;
        MouseRecordStatusText.Text = presetId == 1 ? "Preset: Lectura natural cargado" : "Preset: Navegación web cargado";
    }

    
    private MousePoint GetInterpolatedPoint(long elapsedMs)
    {
        lock (_mouseTrackLock)
        {
            if (_recordedMouseTrack.Count == 0) return new MousePoint(100, 100, 0);
            if (_recordedMouseTrack.Count == 1) return _recordedMouseTrack[0];

            int index = _recordedMouseTrack.FindIndex(p => p.ElapsedMs >= elapsedMs);
            if (index <= 0) return _recordedMouseTrack[0];
            if (index >= _recordedMouseTrack.Count) return _recordedMouseTrack[^1];

            var p0 = _recordedMouseTrack[index - 1];
            var p1 = _recordedMouseTrack[index];

            long span = p1.ElapsedMs - p0.ElapsedMs;
            if (span <= 0) return p0;

            double factor = (double)(elapsedMs - p0.ElapsedMs) / span;
            int x = (int)(p0.X + (p1.X - p0.X) * factor);
            int y = (int)(p0.Y + (p1.Y - p0.Y) * factor);
            return new MousePoint(x, y, elapsedMs, p0.IsClick);
        }
    }

    
    private static BitmapSource? _cachedCursorBitmap;
    private static BitmapSource GetSystemCursorBitmap()
    {
        if (_cachedCursorBitmap != null) return _cachedCursorBitmap;

        try
        {
            IntPtr hCursor = IntPtr.Zero;
            string? regCur = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Cursors", "Arrow", null) as string;
            if (!string.IsNullOrEmpty(regCur))
            {
                regCur = Environment.ExpandEnvironmentVariables(regCur);
                if (File.Exists(regCur))
                {
                    hCursor = NativeMethods.LoadCursorFromFile(regCur);
                }
            }

            if (hCursor == IntPtr.Zero)
            {
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string curPath = System.IO.Path.Combine(winDir, "Cursors", "aero_arrow.cur");
                if (File.Exists(curPath))
                {
                    hCursor = NativeMethods.LoadCursorFromFile(curPath);
                }
            }

            if (hCursor == IntPtr.Zero)
            {
                hCursor = NativeMethods.LoadCursor(IntPtr.Zero, (IntPtr)32512);
            }

            if (hCursor != IntPtr.Zero)
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(hCursor, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                NativeMethods.DestroyCursor(hCursor);
                _cachedCursorBitmap = src;
                return _cachedCursorBitmap;
            }
        }
        catch { }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var geo = Geometry.Parse("M0,0 L0,18 L4.5,13.5 L8.5,21.5 L11,20 L7,12.5 L13.5,12.5 Z");
            dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1.2), geo);
        }
        var rtb = new RenderTargetBitmap(32, 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        _cachedCursorBitmap = rtb;
        return _cachedCursorBitmap;
    }

    private Window CreateCursorWindow(bool excludeFromCapture)
    {
        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            Width = 32,
            Height = 32,
            Focusable = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000
        };

        var img = new Image
        {
            Source = GetSystemCursorBitmap(),
            Width = 32,
            Height = 32,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        win.Content = img;

        win.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(win).Handle;
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                new IntPtr(exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));

            if (excludeFromCapture)
            {
                NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            }
        };

        return win;
    }

    
    private void OnToggleDecoyClick(object sender, RoutedEventArgs e)
    {
        ToggleCursorDecoy();
    }

    
        private void UpdateDrawingToolButtons(Button? activeButton)
    {
        var activeStyle = (Style)FindResource("PrimaryPillBtn");
        var inactiveStyle = (Style)FindResource("SecondaryPillBtn");

        if (ToolFreehandBtn != null) ToolFreehandBtn.Style = inactiveStyle;
        if (ToolLineBtn != null) ToolLineBtn.Style = inactiveStyle;
        if (ToolRectBtn != null) ToolRectBtn.Style = inactiveStyle;
        if (ToolCircleBtn != null) ToolCircleBtn.Style = inactiveStyle;

        if (activeButton != null)
            activeButton.Style = activeStyle;
    }

    
    private void OnDrawFreehandClick(object sender, RoutedEventArgs e)
    {
        if (_currentShapeTool == ShapeToolMode.Freehand)
        {
            // Deselect tool
            _currentShapeTool = ShapeToolMode.None;
            UpdateDrawingToolButtons(null);
            HideShapeOverlays();
            if (DrawingCanvas != null) DrawingCanvas.Cursor = Cursors.Arrow;
            MouseRecordStatusText.Text = "Pizarra en espera: selecciona una herramienta para comenzar a dibujar";
        }
        else
        {
            _currentShapeTool = ShapeToolMode.Freehand;
            HideShapeOverlays();
            UpdateDrawingToolButtons(ToolFreehandBtn);
            if (DrawingCanvas != null) DrawingCanvas.Cursor = Cursors.Cross;
            MouseRecordStatusText.Text = "Modo: Dibujo libre a mano alzada activo";
        }
    }

    
    private void OnDrawLineClick(object sender, RoutedEventArgs e)
    {
        if (_currentShapeTool == ShapeToolMode.Line)
        {
            // Deselect tool
            _currentShapeTool = ShapeToolMode.None;
            UpdateDrawingToolButtons(null);
            HideShapeOverlays();
            if (DrawingCanvas != null) DrawingCanvas.Cursor = Cursors.Arrow;
            MouseRecordStatusText.Text = "Pizarra en espera: selecciona una herramienta para comenzar a dibujar";
        }
        else
        {
            _currentShapeTool = ShapeToolMode.Line;
            DrawingPolyline.Points.Clear();
            _lineStart = new(35, 82);
            _lineEnd = new(289, 82);
            UpdateInteractiveLine();
            UpdateDrawingToolButtons(ToolLineBtn);
            if (DrawingCanvas != null) DrawingCanvas.Cursor = Cursors.Cross;
            MouseRecordStatusText.Text = "Línea: Arrastra los extremos para ajustar o el centro para mover";
        }
    }

    
    private void OnDrawRectClick(object sender, RoutedEventArgs e)
    {
        if (_currentShapeTool == ShapeToolMode.Rect)
        {
            // Deselect tool
            _currentShapeTool = ShapeToolMode.None;
            UpdateDrawingToolButtons(null);
            HideShapeOverlays();
            if (DrawingCanvas != null) DrawingCanvas.Cursor = Cursors.Arrow;
            MouseRecordStatusText.Text = "Pizarra en espera: selecciona una herramienta para comenzar a dibujar";
        }
        else
        {
            _currentShapeTool = ShapeToolMode.Rect;
            DrawingPolyline.Points.Clear();
            _rectBounds = new(40, 25, 244, 115);
            UpdateInteractiveRect();
            UpdateDrawingToolButtons(ToolRectBtn);
            if (DrawingCanvas != null) DrawingCanvas.Cursor = Cursors.Cross;
            MouseRecordStatusText.Text = "Rectángulo: Arrastra las esquinas para cambiar tamaño o el interior para mover";
        }
    }

    
    private void OnDrawCircleClick(object sender, RoutedEventArgs e)
    {
        if (_currentShapeTool == ShapeToolMode.Circle)
        {
            // Deselect tool
            _currentShapeTool = ShapeToolMode.None;
            UpdateDrawingToolButtons(null);
            HideShapeOverlays();
            if (DrawingCanvas != null) DrawingCanvas.Cursor = Cursors.Arrow;
            MouseRecordStatusText.Text = "Pizarra en espera: selecciona una herramienta para comenzar a dibujar";
        }
        else
        {
            _currentShapeTool = ShapeToolMode.Circle;
            DrawingPolyline.Points.Clear();
            _circleCenter = new(162, 82);
            _circleRadius = 55;
            UpdateInteractiveCircle();
            UpdateDrawingToolButtons(ToolCircleBtn);
            if (DrawingCanvas != null) DrawingCanvas.Cursor = Cursors.Cross;
            MouseRecordStatusText.Text = "Círculo: Arrastra el centro para mover o el punto perimetral para cambiar radio";
        }
    }

    
    private void OnClearDrawingClick(object sender, RoutedEventArgs e)
    {
        OnClearMouseTrackClick(sender, e);
        _currentShapeTool = ShapeToolMode.None;
        UpdateDrawingToolButtons(null);
        HideShapeOverlays();
        if (DrawingCanvas != null) DrawingCanvas.Cursor = Cursors.Arrow;
        MouseRecordStatusText.Text = "Pizarra y ruta borradas. Selecciona una herramienta para dibujar.";
    }

    
    private void HideShapeOverlays()
    {
        InteractiveLine.Visibility = Visibility.Collapsed;
        InteractiveRect.Visibility = Visibility.Collapsed;
        InteractiveCircle.Visibility = Visibility.Collapsed;
        Handle1.Visibility = Visibility.Collapsed;
        Handle2.Visibility = Visibility.Collapsed;
        Handle3.Visibility = Visibility.Collapsed;
        Handle4.Visibility = Visibility.Collapsed;
    }

    
    private void UpdateInteractiveLine()
    {
        HideShapeOverlays();
        InteractiveLine.X1 = _lineStart.X;
        InteractiveLine.Y1 = _lineStart.Y;
        InteractiveLine.X2 = _lineEnd.X;
        InteractiveLine.Y2 = _lineEnd.Y;
        InteractiveLine.Visibility = Visibility.Visible;

        PositionHandle(Handle1, _lineStart.X, _lineStart.Y);
        PositionHandle(Handle2, _lineEnd.X, _lineEnd.Y);
    }

    
    private void UpdateInteractiveRect()
    {
        HideShapeOverlays();
        Canvas.SetLeft(InteractiveRect, _rectBounds.X);
        Canvas.SetTop(InteractiveRect, _rectBounds.Y);
        InteractiveRect.Width = Math.Max(10, _rectBounds.Width);
        InteractiveRect.Height = Math.Max(10, _rectBounds.Height);
        InteractiveRect.Visibility = Visibility.Visible;

        PositionHandle(Handle1, _rectBounds.Left, _rectBounds.Top);
        PositionHandle(Handle2, _rectBounds.Right, _rectBounds.Top);
        PositionHandle(Handle3, _rectBounds.Right, _rectBounds.Bottom);
        PositionHandle(Handle4, _rectBounds.Left, _rectBounds.Bottom);
    }

    
    private void UpdateInteractiveCircle()
    {
        HideShapeOverlays();
        double d = _circleRadius * 2;
        Canvas.SetLeft(InteractiveCircle, _circleCenter.X - _circleRadius);
        Canvas.SetTop(InteractiveCircle, _circleCenter.Y - _circleRadius);
        InteractiveCircle.Width = d;
        InteractiveCircle.Height = d;
        InteractiveCircle.Visibility = Visibility.Visible;

        PositionHandle(Handle1, _circleCenter.X, _circleCenter.Y);
        PositionHandle(Handle2, _circleCenter.X + _circleRadius, _circleCenter.Y);
    }

    
    private void PositionHandle(Ellipse h, double x, double y)
    {
        Canvas.SetLeft(h, x - 5);
        Canvas.SetTop(h, y - 5);
        h.Visibility = Visibility.Visible;
    }

    
    private void OnHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is Ellipse h)
        {
            if (h == Handle1) _draggedHandleIndex = 1;
            else if (h == Handle2) _draggedHandleIndex = 2;
            else if (h == Handle3) _draggedHandleIndex = 3;
            else if (h == Handle4) _draggedHandleIndex = 4;

            _lastMouseCanvasPos = e.GetPosition(DrawingCanvas);
            DrawingCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    
    private void OnDrawingCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_isMouseRecording || _currentShapeTool == ShapeToolMode.None) return;

        var pt = e.GetPosition(DrawingCanvas);
        _lastMouseCanvasPos = pt;

        if (_currentShapeTool == ShapeToolMode.Freehand)
        {
            _isDrawingFreehand = true;
            DrawingCanvas.CaptureMouse();
            DrawingPolyline.Points.Clear();
            DrawingPolyline.Points.Add(pt);
        }
        else
        {
            bool clickedInside = false;
            if (_currentShapeTool == ShapeToolMode.Line)
            {
                clickedInside = DistanceToSegment(pt, _lineStart, _lineEnd) < 14;
            }
            else if (_currentShapeTool == ShapeToolMode.Rect)
            {
                clickedInside = _rectBounds.Contains(pt);
            }
            else if (_currentShapeTool == ShapeToolMode.Circle)
            {
                double dist = Math.Sqrt(Math.Pow(pt.X - _circleCenter.X, 2) + Math.Pow(pt.Y - _circleCenter.Y, 2));
                clickedInside = dist <= _circleRadius + 8;
            }

            if (clickedInside)
            {
                _isDraggingShapeBody = true;
                DrawingCanvas.CaptureMouse();
            }
        }
    }

    
    private void OnDrawingCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var pt = e.GetPosition(DrawingCanvas);
        double cw = DrawingCanvas.ActualWidth > 0 ? DrawingCanvas.ActualWidth : 324;
        double ch = DrawingCanvas.ActualHeight > 0 ? DrawingCanvas.ActualHeight : 165;
        double x = Math.Clamp(pt.X, 0, cw);
        double y = Math.Clamp(pt.Y, 0, ch);

        if (_isDrawingFreehand && e.LeftButton == MouseButtonState.Pressed)
        {
            if (DrawingPolyline.Points.Count == 0 ||
                Math.Abs(DrawingPolyline.Points[^1].X - x) > 1.5 ||
                Math.Abs(DrawingPolyline.Points[^1].Y - y) > 1.5)
            {
                DrawingPolyline.Points.Add(new System.Windows.Point(x, y));
            }
        }
        else if (_draggedHandleIndex > 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            if (_currentShapeTool == ShapeToolMode.Line)
            {
                if (_draggedHandleIndex == 1) _lineStart = new(x, y);
                else if (_draggedHandleIndex == 2) _lineEnd = new(x, y);
                UpdateInteractiveLine();
            }
            else if (_currentShapeTool == ShapeToolMode.Rect)
            {
                double l = _rectBounds.Left, t = _rectBounds.Top, r = _rectBounds.Right, b = _rectBounds.Bottom;
                if (_draggedHandleIndex == 1) { l = Math.Min(x, r - 10); t = Math.Min(y, b - 10); }
                else if (_draggedHandleIndex == 2) { r = Math.Max(x, l + 10); t = Math.Min(y, b - 10); }
                else if (_draggedHandleIndex == 3) { r = Math.Max(x, l + 10); b = Math.Max(y, t + 10); }
                else if (_draggedHandleIndex == 4) { l = Math.Min(x, r - 10); b = Math.Max(y, t + 10); }
                _rectBounds = new Rect(l, t, Math.Max(10, r - l), Math.Max(10, b - t));
                UpdateInteractiveRect();
            }
            else if (_currentShapeTool == ShapeToolMode.Circle)
            {
                if (_draggedHandleIndex == 1)
                {
                    _circleCenter = new(x, y);
                }
                else if (_draggedHandleIndex == 2)
                {
                    _circleRadius = Math.Clamp(Math.Sqrt(Math.Pow(x - _circleCenter.X, 2) + Math.Pow(y - _circleCenter.Y, 2)), 10, 80);
                }
                UpdateInteractiveCircle();
            }
        }
        else if (_isDraggingShapeBody && e.LeftButton == MouseButtonState.Pressed)
        {
            double dx = pt.X - _lastMouseCanvasPos.X;
            double dy = pt.Y - _lastMouseCanvasPos.Y;
            _lastMouseCanvasPos = pt;

            if (_currentShapeTool == ShapeToolMode.Line)
            {
                _lineStart = new System.Windows.Point(Math.Clamp(_lineStart.X + dx, 0, cw), Math.Clamp(_lineStart.Y + dy, 0, ch));
                _lineEnd = new System.Windows.Point(Math.Clamp(_lineEnd.X + dx, 0, cw), Math.Clamp(_lineEnd.Y + dy, 0, ch));
                UpdateInteractiveLine();
            }
            else if (_currentShapeTool == ShapeToolMode.Rect)
            {
                double nx = Math.Clamp(_rectBounds.X + dx, 0, cw - _rectBounds.Width);
                double ny = Math.Clamp(_rectBounds.Y + dy, 0, ch - _rectBounds.Height);
                _rectBounds = new Rect(nx, ny, _rectBounds.Width, _rectBounds.Height);
                UpdateInteractiveRect();
            }
            else if (_currentShapeTool == ShapeToolMode.Circle)
            {
                double nx = Math.Clamp(_circleCenter.X + dx, _circleRadius, cw - _circleRadius);
                double ny = Math.Clamp(_circleCenter.Y + dy, _circleRadius, ch - _circleRadius);
                _circleCenter = new(nx, ny);
                UpdateInteractiveCircle();
            }
        }
    }

    
    private void OnDrawingCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDrawingFreehand = false;
        _isDraggingShapeBody = false;
        _draggedHandleIndex = 0;
        DrawingCanvas.ReleaseMouseCapture();
    }

    
    private double DistanceToSegment(System.Windows.Point p, System.Windows.Point a, System.Windows.Point b)
    {
        double l2 = Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2);
        if (l2 == 0) return Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2));
        double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / l2));
        var projection = new System.Windows.Point(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
        return Math.Sqrt(Math.Pow(p.X - projection.X, 2) + Math.Pow(p.Y - projection.Y, 2));
    }

    
    private void OnApplyDrawingToTrackClick(object sender, RoutedEventArgs e)
    {
        List<System.Windows.Point> sampledPoints = new();
        string shapeName = "personalizada";

        double cw = DrawingCanvas.ActualWidth > 10 ? DrawingCanvas.ActualWidth : 350.0;
        double ch = DrawingCanvas.ActualHeight > 10 ? DrawingCanvas.ActualHeight : 145.0;

        if (_currentShapeTool == ShapeToolMode.Freehand)
        {
            if (DrawingPolyline.Points.Count < 2)
            {
                MessageBox.Show("Dibuja primero una trayectoria libre o selecciona una forma (Línea, Rectángulo, Círculo).", "CastDecoy", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            sampledPoints.AddRange(DrawingPolyline.Points);
            shapeName = "mano alzada";
        }
        else if (_currentShapeTool == ShapeToolMode.Line)
        {
            shapeName = "línea recta";
            int steps = 50;
            for (int i = 0; i <= steps; i++)
            {
                double factor = (double)i / steps;
                sampledPoints.Add(new System.Windows.Point(
                    _lineStart.X + (_lineEnd.X - _lineStart.X) * factor,
                    _lineStart.Y + (_lineEnd.Y - _lineStart.Y) * factor));
            }
        }
        else if (_currentShapeTool == ShapeToolMode.Rect)
        {
            shapeName = "rectángulo perimetral";
            double l = _rectBounds.Left, t = _rectBounds.Top, r = _rectBounds.Right, b = _rectBounds.Bottom;
            int segSteps = 25;
            for (int i = 0; i < segSteps; i++) sampledPoints.Add(new(l + (r - l) * (i / (double)segSteps), t));
            for (int i = 0; i < segSteps; i++) sampledPoints.Add(new(r, t + (b - t) * (i / (double)segSteps)));
            for (int i = 0; i < segSteps; i++) sampledPoints.Add(new(r - (r - l) * (i / (double)segSteps), b));
            for (int i = 0; i <= segSteps; i++) sampledPoints.Add(new(l, b - (b - t) * (i / (double)segSteps)));
        }
        else if (_currentShapeTool == ShapeToolMode.Circle)
        {
            shapeName = "órbita circular";
            int steps = 70;
            for (int i = 0; i <= steps; i++)
            {
                double angle = (2 * Math.PI * i) / steps;
                sampledPoints.Add(new System.Windows.Point(
                    _circleCenter.X + _circleRadius * Math.Cos(angle),
                    _circleCenter.Y + _circleRadius * Math.Sin(angle)));
            }
        }

        if (sampledPoints.Count < 2) return;

        if (_isMousePlaying) StopMousePlayback();
        if (_isMouseRecording) StopMouseRecording();

        int screenW = NativeMethods.GetSystemMetrics(0);
        int screenH = NativeMethods.GetSystemMetrics(1);
        if (screenW <= 0) screenW = 2560;
        if (screenH <= 0) screenH = 1600;

        lock (_mouseTrackLock)
        {
            _recordedMouseTrack.Clear();
            long t = 0;
            foreach (var pt in sampledPoints)
            {
                int sx = (int)Math.Clamp((pt.X / cw) * screenW, 0, screenW);
                int sy = (int)Math.Clamp((pt.Y / ch) * screenH, 0, screenH);
                _recordedMouseTrack.Add(new MousePoint(sx, sy, t));
                t += 25;
            }
        }

        double padW = MousepadMiniCanvas != null && MousepadMiniCanvas.ActualWidth > 10 ? MousepadMiniCanvas.ActualWidth : 350.0;
        double padH = MousepadMiniCanvas != null && MousepadMiniCanvas.ActualHeight > 10 ? MousepadMiniCanvas.ActualHeight : 145.0;
        MousepadTrackLine.Points.Clear();
        foreach (var pt in sampledPoints)
        {
            double mx = Math.Clamp(pt.X / cw, 0, 1) * Math.Max(10, padW - 12);
            double my = Math.Clamp(pt.Y / ch, 0, 1) * Math.Max(10, padH - 12);
            MousepadTrackLine.Points.Add(new System.Windows.Point(mx, my));
        }

        if (sampledPoints.Count > 0)
        {
            double mx = Math.Clamp(sampledPoints[0].X / cw, 0, 1) * Math.Max(10, padW - 12);
            double my = Math.Clamp(sampledPoints[0].Y / ch, 0, 1) * Math.Max(10, padH - 12);
            Canvas.SetLeft(MousepadCursorDot, mx);
            Canvas.SetTop(MousepadCursorDot, my);
        }

        int count = _recordedMouseTrack.Count;
        double durSec = (count * 25) / 1000.0;
        MousePointsText.Text = $"{count} pts ({durSec:F1}s)";
        MouseDurationText.Text = $"{durSec:F1} s";
        MousePlayBtn.IsEnabled = true;
        MouseRecordStatusDot.Fill = AccentBlueBrush;
        MouseRecordStatusText.Text = $"Ruta de {shapeName} cargada a pantalla completa ({screenW}x{screenH})";
    }

    
    private void UpdateMousepadVisual(double screenX, double screenY, bool isClick = false)
    {
        try
        {
            int screenW = NativeMethods.GetSystemMetrics(0);
            int screenH = NativeMethods.GetSystemMetrics(1);
            if (screenW <= 0) screenW = (int)SystemParameters.PrimaryScreenWidth;
            if (screenH <= 0) screenH = (int)SystemParameters.PrimaryScreenHeight;
            if (screenW <= 0) screenW = 2560;
            if (screenH <= 0) screenH = 1600;

            double padW = MousepadMiniCanvas != null && MousepadMiniCanvas.ActualWidth > 10 ? MousepadMiniCanvas.ActualWidth : 350.0;
            double padH = MousepadMiniCanvas != null && MousepadMiniCanvas.ActualHeight > 10 ? MousepadMiniCanvas.ActualHeight : 145.0;

            double normX = Math.Clamp(screenX / (double)screenW, 0, 1) * Math.Max(10, padW - 12);
            double normY = Math.Clamp(screenY / (double)screenH, 0, 1) * Math.Max(10, padH - 12);

            if (MousepadCursorDot != null)
            {
                Canvas.SetLeft(MousepadCursorDot, normX);
                Canvas.SetTop(MousepadCursorDot, normY);
            }
            if (MouseCoordinatesText != null)
            {
                MouseCoordinatesText.Text = $"X: {(int)screenX} | Y: {(int)screenY}";
            }

            if (_isMouseLagActive && _showLagGhost && MousepadLagDot != null)
            {
                MousepadLagDot.Visibility = Visibility.Visible;
                double lagNormX = Math.Clamp(_currentLagTargetX / (double)screenW, 0, 1) * Math.Max(10, padW - 12);
                double lagNormY = Math.Clamp(_currentLagTargetY / (double)screenH, 0, 1) * Math.Max(10, padH - 12);
                Canvas.SetLeft(MousepadLagDot, lagNormX);
                Canvas.SetTop(MousepadLagDot, lagNormY);
            }
            else if (MousepadLagDot != null)
            {
                MousepadLagDot.Visibility = Visibility.Collapsed;
            }

            if (isClick)
            {
                Canvas.SetLeft(MousepadClickRipple, normX - 6);
                Canvas.SetTop(MousepadClickRipple, normY - 6);
                MousepadClickRipple.Opacity = 1.0;
                var anim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300));
                MousepadClickRipple.BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }
        catch { }
    }

    
    private void OnToggleAutoJitterClick(object sender, RoutedEventArgs e)
    {
        ToggleAutoJitter();
    }

    public void ToggleAutoJitter()
    {
        if (_isAutoJitterActive)
        {
            SetMouseLiveMode();
        }
        else
        {
            SetMouseJitterMode();
        }
    }

    private void OnAutoJitterCheckClicked(object? sender, RoutedEventArgs? e)
    {
        ToggleAutoJitter();
    }

    private void OnAutoJitterIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoJitterIntervalCombo == null) return;
        if (AutoJitterIntervalCombo.SelectedIndex == 0) _autoJitterIntervalSeconds = 10;
        else if (AutoJitterIntervalCombo.SelectedIndex == 1) _autoJitterIntervalSeconds = 15;
        else if (AutoJitterIntervalCombo.SelectedIndex == 2) _autoJitterIntervalSeconds = 30;
        else if (AutoJitterIntervalCombo.SelectedIndex == 3) _autoJitterIntervalSeconds = 60;
        else _autoJitterIntervalSeconds = 300;

        if (_autoJitterTimer != null && _autoJitterTimer.IsEnabled)
        {
            _autoJitterTimer.Interval = TimeSpan.FromSeconds(_autoJitterIntervalSeconds);
        }
        if (_isAutoJitterActive)
        {
            AutoJitterStatusText.Text = $"Activo: Pulso cada {_autoJitterIntervalSeconds} s";
        }
    }

    private void StartAutoJitter()
    {
        SetMouseJitterMode();
    }

    private void StopAutoJitter()
    {
        SetMouseLiveMode();
    }

    private void OnToggleOrganicDriftClick(object sender, RoutedEventArgs e)
    {
        ToggleOrganicDrift();
    }

    public void ToggleOrganicDrift()
    {
        if (_isOrganicDriftActive)
        {
            SetMouseLiveMode();
        }
        else
        {
            SetMouseDriftMode();
        }
    }

    private void OnOrganicDriftCheckClicked(object sender, RoutedEventArgs e)
    {
        ToggleOrganicDrift();
    }

    private void OnDriftPatternChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriftPatternCombo?.SelectedItem is ComboBoxItem item)
            _driftPattern = item.Content?.ToString() ?? "Oscilación biológica";

        if (_isOrganicDriftActive)
        {
            OrganicDriftStatusText.Text = $"Deriva activa ({_driftPattern})";
        }
    }

    private void StartOrganicDrift()
    {
        SetMouseDriftMode();
    }

    private void StopOrganicDrift()
    {
        SetMouseLiveMode();
    }

    private void UpdateMousepadDriftVisual(double screenX, double screenY)
    {
        if (MousepadDriftDot == null || MousepadMiniCanvas == null) return;
        int screenW = NativeMethods.GetSystemMetrics(0);
        int screenH = NativeMethods.GetSystemMetrics(1);
        if (screenW <= 0) screenW = 2560;
        if (screenH <= 0) screenH = 1600;

        double padW = MousepadMiniCanvas.ActualWidth > 10 ? MousepadMiniCanvas.ActualWidth : 350.0;
        double padH = MousepadMiniCanvas.ActualHeight > 10 ? MousepadMiniCanvas.ActualHeight : 145.0;

        double normX = Math.Clamp(screenX / (double)screenW, 0, 1) * Math.Max(10, padW - 12);
        double normY = Math.Clamp(screenY / (double)screenH, 0, 1) * Math.Max(10, padH - 12);

        MousepadDriftDot.Visibility = _isOrganicDriftActive ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(MousepadDriftDot, normX);
        Canvas.SetTop(MousepadDriftDot, normY);
    }

    public void InitMouseControls()
    {
        CenterMousepadDot();
        if (MousepadMiniCanvas != null)
        {
            MousepadMiniCanvas.Loaded += (_, _) => CenterMousepadDot();
            MousepadMiniCanvas.SizeChanged += (_, _) =>
            {
                if (!_isMouseRecording && !_isMousePlaying && _recordedMouseTrack.Count == 0)
                {
                    CenterMousepadDot();
                }
            };
        }
    }

    public void CenterMousepadDot()
    {
        int screenW = NativeMethods.GetSystemMetrics(0);
        int screenH = NativeMethods.GetSystemMetrics(1);
        if (screenW <= 0) screenW = (int)SystemParameters.PrimaryScreenWidth;
        if (screenH <= 0) screenH = (int)SystemParameters.PrimaryScreenHeight;
        if (screenW <= 0) screenW = 2560;
        if (screenH <= 0) screenH = 1600;

        if (MouseCoordinatesText != null)
        {
            MouseCoordinatesText.Text = $"X: {screenW / 2} | Y: {screenH / 2}";
        }

        if (MousepadMiniCanvas != null && MousepadCursorDot != null)
        {
            double padW = MousepadMiniCanvas.ActualWidth > 10 ? MousepadMiniCanvas.ActualWidth : 370.0;
            double padH = MousepadMiniCanvas.ActualHeight > 10 ? MousepadMiniCanvas.ActualHeight : 195.0;
            double dotW = MousepadCursorDot.Width > 0 ? MousepadCursorDot.Width : 10.0;
            double dotH = MousepadCursorDot.Height > 0 ? MousepadCursorDot.Height : 10.0;
            double cx = Math.Max(0, (padW - dotW) / 2.0);
            double cy = Math.Max(0, (padH - dotH) / 2.0);
            Canvas.SetLeft(MousepadCursorDot, cx);
            Canvas.SetTop(MousepadCursorDot, cy);
        }
    }

}
