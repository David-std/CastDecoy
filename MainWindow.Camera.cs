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
    private int _targetCamFps = 30;
    private readonly List<VideoCharacteristics> _currentCamCharacteristics = new();
    private long _lastCamRenderTicks = 0;
    private readonly List<CaptureDeviceDescriptor> _camDescriptors = new();
    private CaptureDevice? _activeCaptureDevice;
    private CancellationTokenSource? _camCts;
    private bool _isCamActive = false;
    private volatile bool _isCamFrozen = false;
    private volatile bool _isBlackFrame = false;
    private volatile bool _isLooping = false;
    private volatile bool _isLoopModeActive = false;
    private byte[]? _frozenFrameBytes = null;
    private int _loopDurationSeconds = 15;
    private readonly List<byte[]> _loopBuffer = new();
    private readonly object _loopLock = new();
    private int _loopPlaybackIndex = 0;
    private DispatcherTimer? _loopPlaybackTimer;
    private Window? _camPopoutWindow;
    private System.Windows.Controls.Image? _popoutImage;
    private System.Windows.Shapes.Rectangle? _popoutBlackOverlay;
    private byte[]? _lastFrameBytes;
    private bool _isGlitchActive = false;
    private SolidColorBrush _selectedColorBrush = new(MediaColor.FromRgb(0, 0, 0));
    private volatile bool _isCamStuttering = false;
    private Stopwatch _stutterStopwatch = new();
    private int _stutterCycleSeconds = 15;
    private int _stutterFrameCount = 0;
    private byte[]? _stutterHoldFrame = null;

    
    private void InitCameraControls()
    {
        LoopDurationComboBox.Items.Clear();
        LoopDurationComboBox.Items.Add("5 s");
        LoopDurationComboBox.Items.Add("10 s");
        LoopDurationComboBox.Items.Add("15 s");
        LoopDurationComboBox.Items.Add("30 s");
        LoopDurationComboBox.Items.Add("60 s");
        LoopDurationComboBox.SelectedIndex = 2;

        ColorSelectComboBox.Items.Clear();
        ColorSelectComboBox.Items.Add("Negro (Sin señal)");
        ColorSelectComboBox.Items.Add("Gris oscuro");
        ColorSelectComboBox.Items.Add("Azul");
        ColorSelectComboBox.Items.Add("Verde chroma");
        ColorSelectComboBox.SelectedIndex = 0;

        ScanCameraDevices();
    }

    
    private void ScanCameraDevices()
    {
        CamDeviceComboBox.Items.Clear();
        _camDescriptors.Clear();

        try
        {
            var devices = new CaptureDevices();
            var allDescriptors = devices.EnumerateDescriptors().ToList();

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sorted = allDescriptors
                .OrderByDescending(d => d.DeviceType == DeviceTypes.DirectShow)
                .ThenByDescending(d => d.DeviceType == DeviceTypes.MediaFoundation)
                .ToList();

            foreach (var desc in sorted)
            {
                string displayName = desc.Name;
                if (!seenNames.Contains(displayName))
                {
                    seenNames.Add(displayName);
                    _camDescriptors.Add(desc);
                    CamDeviceComboBox.Items.Add(displayName);
                }
            }

            if (CamDeviceComboBox.Items.Count > 0)
            {
                CamDeviceComboBox.SelectedIndex = 0;
                if (_camDescriptors.Count > 0)
                {
                    UpdateCamFormats(_camDescriptors[0]);
                }
            }
                        else
            {
                CamDeviceComboBox.Items.Add("USB2.0 HD UVC WebCam (Predeterminada)");
                CamDeviceComboBox.Items.Add("OBS Virtual Camera");
                CamDeviceComboBox.SelectedIndex = 0;

                if (CamFormatComboBox.Items.Count == 0)
                {
                    CamFormatComboBox.Items.Add("1920x1080 (60 fps)");
                    CamFormatComboBox.Items.Add("1280x720 (30 fps)");
                    CamFormatComboBox.Items.Add("640x480 (30 fps)");
                    CamFormatComboBox.SelectedIndex = 1;
                }
            }
        }
        catch { }
    }

    
    private void OnRefreshDevicesClick(object sender, RoutedEventArgs e)
    {
        ScanCameraDevices();
    }

    
    private async void OnCamDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CamDeviceComboBox.SelectedIndex >= 0 && CamDeviceComboBox.SelectedIndex < _camDescriptors.Count)
        {
            var desc = _camDescriptors[CamDeviceComboBox.SelectedIndex];
            UpdateCamFormats(desc);

            if (_isCamActive)
            {
                var charact = _currentCamCharacteristics.ElementAtOrDefault(CamFormatComboBox.SelectedIndex);
                await StartCaptureAsync(desc, charact);
            }
        }
    }

    
    private void UpdateCamFormats(CaptureDeviceDescriptor desc)
    {
        _currentCamCharacteristics.Clear();
        CamFormatComboBox.Items.Clear();

        var sorted = desc.Characteristics
            .OrderByDescending(c => c.Width * c.Height)
            .ThenByDescending(c => c.FramesPerSecond.Denominator > 0 ? (double)c.FramesPerSecond.Numerator / c.FramesPerSecond.Denominator : 30)
            .ToList();

        var seen = new HashSet<string>();
        foreach (var c in sorted)
        {
            int fps = c.FramesPerSecond.Denominator > 0
                ? (int)Math.Round((double)c.FramesPerSecond.Numerator / c.FramesPerSecond.Denominator)
                : 30;
            if (fps < 1) fps = 30;
            string label = $"{c.Width}x{c.Height} ({fps} fps)";
            if (!seen.Contains(label))
            {
                seen.Add(label);
                _currentCamCharacteristics.Add(c);
                CamFormatComboBox.Items.Add(label);
            }
        }

        if (CamFormatComboBox.Items.Count > 0)
        {
            CamFormatComboBox.SelectedIndex = 0;
        }
    }

    
    private async void OnCamFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CamFormatComboBox.SelectedIndex < 0 || CamFormatComboBox.SelectedIndex >= _currentCamCharacteristics.Count) return;

        var charact = _currentCamCharacteristics[CamFormatComboBox.SelectedIndex];
        int maxFps = charact.FramesPerSecond.Denominator > 0
            ? (int)Math.Round((double)charact.FramesPerSecond.Numerator / charact.FramesPerSecond.Denominator)
            : 30;
        if (maxFps < 1) maxFps = 30;

        CamFpsSlider.Maximum = maxFps;
        CamFpsSlider.Value = maxFps;
        FpsDisplayLabel.Text = $"{maxFps} FPS";
        _targetCamFps = maxFps;

        if (_isCamActive && CamDeviceComboBox.SelectedIndex >= 0 && CamDeviceComboBox.SelectedIndex < _camDescriptors.Count)
        {
            await StartCaptureAsync(_camDescriptors[CamDeviceComboBox.SelectedIndex], charact);
        }
    }

    
    private void OnLoopDurationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LoopDurationComboBox.SelectedItem is string sel)
        {
            string num = sel.Replace("s", "").Trim();
            if (int.TryParse(num, out int s))
            {
                _loopDurationSeconds = s;
                lock (_loopLock)
                {
                    int maxFrames = _loopDurationSeconds * 30;
                    if (_loopBuffer.Count > maxFrames)
                    {
                        _loopBuffer.RemoveRange(0, _loopBuffer.Count - maxFrames);
                    }
                }
            }
        }
    }

    
    private void OnColorSelectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColorSelectComboBox.SelectedIndex == 0)
            _selectedColorBrush = new SolidColorBrush(MediaColor.FromRgb(0, 0, 0));
        else if (ColorSelectComboBox.SelectedIndex == 1)
            _selectedColorBrush = new SolidColorBrush(MediaColor.FromRgb(0x1f, 0x20, 0x23));
        else if (ColorSelectComboBox.SelectedIndex == 2)
            _selectedColorBrush = new SolidColorBrush(MediaColor.FromRgb(0x1a, 0x73, 0xe8));
        else if (ColorSelectComboBox.SelectedIndex == 3)
            _selectedColorBrush = new SolidColorBrush(MediaColor.FromRgb(0x00, 0xff, 0x00));

        if (_isBlackFrame)
        {
            ColorFrameOverlay.Fill = _selectedColorBrush;
            if (_popoutBlackOverlay != null) _popoutBlackOverlay.Fill = _selectedColorBrush;
        }
    }

    
    private void OnApplyColorClick(object sender, RoutedEventArgs e)
    {
        ModeColorRadio.IsChecked = true;
        OnCamModeRadioClicked(ModeColorRadio, null!);
    }

    
    private void OnLiveOptionClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ModeLiveRadio.IsChecked = true;
        OnCamModeRadioClicked(ModeLiveRadio, null!);
    }

    
    private void OnFreezeOptionClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ModeFreezeRadio.IsChecked = true;
        OnCamModeRadioClicked(ModeFreezeRadio, null!);
    }

    
    private void OnColorOptionClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ModeColorRadio.IsChecked = true;
        OnCamModeRadioClicked(ModeColorRadio, null!);
    }

    
    private void OnLoopOptionClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ModeLoopRadio.IsChecked = true;
        SetLoopMode(true);
    }

    
    private void ClearAllCamEffects()
    {
        _isLoopModeActive = false;
        _isCamFrozen = false;
        _frozenFrameBytes = null;
        _isBlackFrame = false;
        StopStutterMode();
        StopLoopPlayback();

        if (ColorFrameOverlay != null) ColorFrameOverlay.Visibility = Visibility.Collapsed;
        if (GlitchFrameOverlay != null) GlitchFrameOverlay.Visibility = Visibility.Collapsed;
        if (_popoutBlackOverlay != null) _popoutBlackOverlay.Visibility = Visibility.Collapsed;
        _isGlitchActive = false;

        if (FreezeCamToggleBtn != null)
        {
            FreezeCamToggleBtn.Content = "Congelar";
            FreezeCamToggleBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
        if (StutterCamBtn != null)
        {
            StutterCamBtn.Content = "Iniciar";
            StutterCamBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
        if (ApplyColorBtn != null)
        {
            ApplyColorBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
        if (LoopToggleBtn != null)
        {
            LoopToggleBtn.Content = "Iniciar";
            LoopToggleBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
        if (GlitchToggleBtn != null)
        {
            GlitchToggleBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
    }

    
        private void OnCamModeRadioClicked(object sender, RoutedEventArgs e)
    {
        if (sender == ModeLiveRadio)
        {
            SetLiveMode();
        }
        else if (sender == ModeFreezeRadio)
        {
            SetFreezeMode(true);
        }
        else if (sender == ModeStutterRadio)
        {
            SetStutterMode(true);
        }
        else if (sender == ModeColorRadio)
        {
            SetColorFrameMode(true);
        }
        else if (sender == ModeLoopRadio)
        {
            SetLoopMode(true);
        }
        else if (sender == ModeGlitchRadio)
        {
            SetGlitchMode(true);
        }
    }

    
    private void SetLiveMode()
    {
        ClearAllCamEffects();
        ModeLiveRadio.IsChecked = true;

        if (_isCamActive)
        {
            CamBadgeText.Text = "En vivo";
            CamStatusDot.Fill = StatusGreenBrush;
            TileCamStatusText.Text = "Cámara en vivo";
            TileCamStatusText.Foreground = InactiveTextBrush;

            if (_lastFrameBytes != null)
            {
                DisplayFrame(_lastFrameBytes);
            }
        }
        else
        {
            CamBadgeText.Text = "Inactiva";
            CamStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0x5f, 0x63, 0x68));
            TileCamStatusText.Text = "Cámara inactiva";
            TileCamStatusText.Foreground = InactiveTextBrush;
        }
    }

    
    private void SetFreezeMode(bool freeze)
    {
        ClearAllCamEffects();

        if (freeze)
        {
            _isCamFrozen = true;
            ModeFreezeRadio.IsChecked = true;
            FreezeCamToggleBtn.Content = "Reanudar";
            FreezeCamToggleBtn.Style = (Style)FindResource("PrimaryPillBtn");
            ApplyColorBtn.Style = (Style)FindResource("SecondaryPillBtn");
            LoopToggleBtn.Content = "Iniciar";
            LoopToggleBtn.Style = (Style)FindResource("SecondaryPillBtn");

            if (_isCamActive)
            {
                CamBadgeText.Text = "Congelado";
                CamStatusDot.Fill = AccentBlueBrush;
                TileCamStatusText.Text = "Fotograma congelado";
                TileCamStatusText.Foreground = AccentBlueBrush;

                if (_lastFrameBytes != null)
                {
                    _frozenFrameBytes = _lastFrameBytes;
                    DisplayFrame(_frozenFrameBytes, isControlledPlayback: true);
                }
            }
            else
            {
                CamBadgeText.Text = "Inactiva";
                CamStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0x5f, 0x63, 0x68));
                TileCamStatusText.Text = "Cámara inactiva";
                TileCamStatusText.Foreground = InactiveTextBrush;
            }
        }
        else
        {
            ModeLiveRadio.IsChecked = true;
            SetLiveMode();
        }
    }

    
    private void SetColorFrameMode(bool active)
    {
        ClearAllCamEffects();
        _isBlackFrame = active;

        if (_isBlackFrame)
        {
            ApplyColorBtn.Style = (Style)FindResource("PrimaryPillBtn");

            if (_isCamActive)
            {
                ColorFrameOverlay.Fill = _selectedColorBrush;
                ColorFrameOverlay.Visibility = Visibility.Visible;
                if (_popoutBlackOverlay != null)
                {
                    _popoutBlackOverlay.Fill = _selectedColorBrush;
                    _popoutBlackOverlay.Visibility = Visibility.Visible;
                }

                string colorName = ColorSelectComboBox.SelectedItem?.ToString() ?? "Negro";
                CamBadgeText.Text = colorName.Contains("Negro") ? "Sin señal" : "Frame de color";
                CamStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0xea, 0x43, 0x35));
                TileCamStatusText.Text = "Color / Sin señal";
                TileCamStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xea, 0x43, 0x35));
            }
            else
            {
                ColorFrameOverlay.Visibility = Visibility.Collapsed;
                CamBadgeText.Text = "Inactiva";
                CamStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0x5f, 0x63, 0x68));
                TileCamStatusText.Text = "Cámara inactiva";
                TileCamStatusText.Foreground = InactiveTextBrush;
            }
        }
        else
        {
            ModeLiveRadio.IsChecked = true;
            SetLiveMode();
        }
    }

    
        private async void SetLoopMode(bool active)
    {
        if (!active)
        {
            _isLoopModeActive = false;
            StopLoopPlayback();
            ModeLiveRadio.IsChecked = true;
            SetLiveMode();
            return;
        }

        ClearAllCamEffects();
        _isLoopModeActive = true;
        ModeLoopRadio.IsChecked = true;

        if (!_isCamActive)
        {
            await ActivateCameraAsync();
        }

        int waitCount = 0;
        while (true)
        {
            int count = 0;
            lock (_loopLock) { count = _loopBuffer.Count; }
            if (count >= 15 || waitCount >= 25) break;
            CamBadgeText.Text = "Grabando búfer...";
            TileCamStatusText.Text = "Grabando bucle...";
            await Task.Delay(100);
            waitCount++;
        }

        lock (_loopLock)
        {
            if (_loopBuffer.Count == 0 && _lastFrameBytes != null)
            {
                _loopBuffer.Add((byte[])_lastFrameBytes.Clone());
            }
        }

        _isCamFrozen = false;
        _isBlackFrame = false;
        ColorFrameOverlay.Visibility = Visibility.Collapsed;
        if (_popoutBlackOverlay != null) _popoutBlackOverlay.Visibility = Visibility.Collapsed;

        FreezeCamToggleBtn.Content = "Congelar";
        FreezeCamToggleBtn.Style = (Style)FindResource("SecondaryPillBtn");
        ApplyColorBtn.Style = (Style)FindResource("SecondaryPillBtn");

        StartLoopPlayback();

        CamBadgeText.Text = $"Bucle ({_loopDurationSeconds}s)";
        CamStatusDot.Fill = StatusAmberBrush;
        LoopToggleBtn.Content = "Detener";
        LoopToggleBtn.Style = (Style)FindResource("PrimaryPillBtn");
        TileCamStatusText.Text = $"Bucle ({_loopDurationSeconds}s)";
        TileCamStatusText.Foreground = StatusAmberBrush;
    }

    
    private async void OnActivateCamClick(object sender, RoutedEventArgs e)
    {
        if (_isCamActive)
        {
            await DeactivateCameraAsync();
        }
        else
        {
            await ActivateCameraAsync();
        }
    }

    
    private async Task ActivateCameraAsync()
    {
        if (_isCamActive) return;

        if (_camDescriptors.Count == 0 || CamDeviceComboBox.SelectedIndex < 0 || CamDeviceComboBox.SelectedIndex >= _camDescriptors.Count)
        {
            ScanCameraDevices();
        }

        if (_camDescriptors.Count == 0 || CamDeviceComboBox.SelectedIndex < 0 || CamDeviceComboBox.SelectedIndex >= _camDescriptors.Count)
        {
            MessageBox.Show("No se detectó ningún dispositivo de captura de video disponible.", "CastDecoy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var desc = _camDescriptors[CamDeviceComboBox.SelectedIndex];
        await StartCaptureAsync(desc);

        _isCamActive = true;

        CamInactivePlaceholder.Visibility = Visibility.Collapsed;

        ActivateCamBtn.Content = "Detener cámara";
        ActivateCamBtn.Style = (Style)FindResource("SecondaryPillBtn");

        FreezeCamToggleBtn.IsEnabled = true;
        ApplyColorBtn.IsEnabled = true;
        LoopToggleBtn.IsEnabled = true;
        PopoutCamBtn.IsEnabled = true;

        if (ModeFreezeRadio.IsChecked == true)
        {
            SetFreezeMode(true);
        }
        else if (ModeColorRadio.IsChecked == true)
        {
            SetColorFrameMode(true);
        }
        else if (ModeLoopRadio.IsChecked == true)
        {
            SetLoopMode(true);
        }
        else
        {
            ModeLiveRadio.IsChecked = true;
            SetLiveMode();
        }
    }

    
    private async Task StartCaptureAsync(CaptureDeviceDescriptor descriptor, VideoCharacteristics? targetCharacteristic = null)
    {
        await StopCaptureDeviceAsync();

        _camCts = new CancellationTokenSource();
        var ct = _camCts.Token;

        var charact = targetCharacteristic
                   ?? _currentCamCharacteristics.ElementAtOrDefault(CamFormatComboBox.SelectedIndex)
                   ?? descriptor.Characteristics.FirstOrDefault(c => c.PixelFormat == FlashCap.PixelFormats.JPEG)
                   ?? descriptor.Characteristics.FirstOrDefault();
        if (charact == null) return;

        try
        {
            _activeCaptureDevice = await descriptor.OpenAsync(charact, async bufferScope =>
            {
                if (ct.IsCancellationRequested) return;

                byte[] imageBytes = bufferScope.Buffer.ExtractImage();
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    if (!_isCamFrozen)
                    {
                        _lastFrameBytes = imageBytes;
                    }

                    if (!_isLooping && !_isLoopModeActive)
                    {
                        lock (_loopLock)
                        {
                            _loopBuffer.Add((byte[])imageBytes.Clone());
                            int maxFrames = _loopDurationSeconds * 30;
                            if (_loopBuffer.Count > maxFrames)
                            {
                                _loopBuffer.RemoveRange(0, _loopBuffer.Count - maxFrames);
                            }
                        }

                        int currentSecs = _loopBuffer.Count / 30;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            LoopBufferBadge.Visibility = Visibility.Visible;
                            LoopBufferText.Text = $"Buffer {currentSecs}s / {_loopDurationSeconds}s";
                        });
                    }

                                        long now = Stopwatch.GetTimestamp();
                    long elapsedTicks = now - _lastCamRenderTicks;
                    long minTicks = Stopwatch.Frequency / Math.Max(1, _targetCamFps);
                    if (elapsedTicks < minTicks)
                    {
                        return;
                    }
                    _lastCamRenderTicks = now;
                    
                    // 1. Feed original raw webcam to top camera
                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            using var ms = new MemoryStream(imageBytes);
                            var origBmp = new BitmapImage();
                            origBmp.BeginInit();
                            origBmp.CacheOption = BitmapCacheOption.OnLoad;
                            origBmp.StreamSource = ms;
                            origBmp.EndInit();
                            origBmp.Freeze();
                            CamOriginalPreviewImage.Source = origBmp;
                            CamOriginalInactivePlaceholder.Visibility = Visibility.Collapsed;
                        }
                        catch { }
                    });

                    // 2. Feed modified output to bottom camera (NEVER leak during loop playback)
                    if (_isLoopModeActive || _isLooping)
                    {
                        // Output viewport is strictly controlled by _loopPlaybackTimer!
                    }
                    else if (_isCamStuttering)
                    {
                        long elapsed = _stutterStopwatch.ElapsedMilliseconds % (long)(_stutterCycleSeconds * 1000);
                        double phase = (double)elapsed / (_stutterCycleSeconds * 1000.0);
                        _stutterFrameCount++;

                        if (phase < 0.25)
                        {
                            _stutterHoldFrame = imageBytes;
                            await Dispatcher.InvokeAsync(() => DisplayFrame(imageBytes, isControlledPlayback: false));
                        }
                        else if (phase < 0.55)
                        {
                            if ((_stutterFrameCount % 4) == 0)
                            {
                                _stutterHoldFrame = imageBytes;
                                await Dispatcher.InvokeAsync(() => DisplayFrame(imageBytes, isControlledPlayback: false));
                            }
                        }
                        else if (phase < 0.85)
                        {
                            if ((_stutterFrameCount % 12) == 0)
                            {
                                _stutterHoldFrame = imageBytes;
                                await Dispatcher.InvokeAsync(() => DisplayFrame(imageBytes, isControlledPlayback: false));
                            }
                        }
                        else
                        {
                            if (_stutterHoldFrame != null)
                            {
                                await Dispatcher.InvokeAsync(() => DisplayFrame(_stutterHoldFrame, isControlledPlayback: true));
                            }
                        }
                    }
                    else if (!_isCamFrozen && !_isBlackFrame && !_isLooping)
                    {
                        await Dispatcher.InvokeAsync(() => DisplayFrame(imageBytes, isControlledPlayback: false));
                    }
                }
            }, ct);

            await _activeCaptureDevice.StartAsync();
        }
        catch { }
    }

    
    private async Task StopCaptureDeviceAsync()
    {
        _camCts?.Cancel();
        if (_activeCaptureDevice != null)
        {
            var dev = _activeCaptureDevice;
            _activeCaptureDevice = null;
            try { await dev.StopAsync(); } catch { }
            try { dev.Dispose(); } catch { }
        }
    }

    
    private async Task DeactivateCameraAsync()
    {
        _isCamActive = false;
        _isCamFrozen = false;
        _isBlackFrame = false;
        StopLoopPlayback();

        await StopCaptureDeviceAsync();

        if (_camPopoutWindow != null && _camPopoutWindow.IsVisible)
        {
            _camPopoutWindow.Close();
            _camPopoutWindow = null;
        }

        CamPreviewImage.Source = null;
        CamInactivePlaceholder.Visibility = Visibility.Visible;
        CamOriginalPreviewImage.Source = null;
        CamOriginalInactivePlaceholder.Visibility = Visibility.Visible;
        StopStutterMode();
        ColorFrameOverlay.Visibility = Visibility.Collapsed;
        LoopBufferBadge.Visibility = Visibility.Collapsed;

        ActivateCamBtn.Content = "Activar cámara";
        ActivateCamBtn.Style = (Style)FindResource("PrimaryPillBtn");

        FreezeCamToggleBtn.IsEnabled = false;
        ApplyColorBtn.IsEnabled = false;
        LoopToggleBtn.IsEnabled = false;
        PopoutCamBtn.IsEnabled = false;

        CamBadgeText.Text = "Inactiva";
        CamStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0x5f, 0x63, 0x68));
        TileCamStatusText.Text = "Cámara inactiva";
        TileCamStatusText.Foreground = InactiveTextBrush;
    }

    
    private void DisplayFrame(byte[] imageBytes, bool isControlledPlayback = false)
    {
        if (!isControlledPlayback && (_isCamFrozen || _isBlackFrame || _isLooping || _isLoopModeActive))
        {
            return;
        }

        try
        {
            using var ms = new MemoryStream(imageBytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            CamPreviewImage.Source = bmp;
            if (_popoutImage != null)
            {
                _popoutImage.Source = bmp;
            }
        }
        catch { }
    }

    
    private void OnCamFpsSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FpsDisplayLabel == null) return;
        _targetCamFps = Math.Max(1, (int)Math.Round(e.NewValue));
        FpsDisplayLabel.Text = $"{_targetCamFps} FPS";
    }

    
    private void ToggleFreezeCamera()
    {
        if (!_isCamActive)
        {
            _ = ActivateCameraAsync().ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    ModeFreezeRadio.IsChecked = true;
                    SetFreezeMode(true);
                });
            });
            return;
        }

        if (_isCamFrozen)
        {
            ModeLiveRadio.IsChecked = true;
            SetLiveMode();
        }
        else
        {
            ModeFreezeRadio.IsChecked = true;
            SetFreezeMode(true);
        }
    }

    
    private void OnToggleFreezeCamClick(object sender, RoutedEventArgs e)
    {
        ToggleFreezeCamera();
    }

    
    private void OnToggleLoopClick(object sender, RoutedEventArgs e)
    {
        if (_isLooping)
        {
            ModeLiveRadio.IsChecked = true;
            SetLiveMode();
        }
        else
        {
            ModeLoopRadio.IsChecked = true;
            SetLoopMode(true);
        }
    }

    
        private void StartLoopPlayback()
    {
        _isLoopModeActive = true;
        _isLooping = true;
        _loopPlaybackIndex = 0;
        if (_loopPlaybackTimer != null)
        {
            _loopPlaybackTimer.Stop();
            _loopPlaybackTimer = null;
        }

        int fps = _targetCamFps > 0 ? _targetCamFps : 30;
        int intervalMs = Math.Max(16, 1000 / fps);

        _loopPlaybackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _loopPlaybackTimer.Tick += (_, _) =>
        {
            if (!_isLooping || !_isLoopModeActive) return;

            byte[]? frame = null;
            lock (_loopLock)
            {
                if (_loopBuffer.Count > 0)
                {
                    _loopPlaybackIndex = (_loopPlaybackIndex + 1) % _loopBuffer.Count;
                    frame = _loopBuffer[_loopPlaybackIndex];
                }
            }
            if (frame != null)
            {
                DisplayFrame(frame, isControlledPlayback: true);
            }
        };
        _loopPlaybackTimer.Start();
    }

    
    private void StopLoopPlayback()
    {
        _isLoopModeActive = false;
        _isLooping = false;
        if (_loopPlaybackTimer != null)
        {
            _loopPlaybackTimer.Stop();
            _loopPlaybackTimer = null;
        }
    }

    
    private void OnPopoutCamClick(object sender, RoutedEventArgs e)
    {
        if (_camPopoutWindow != null && _camPopoutWindow.IsVisible)
        {
            _camPopoutWindow.Activate();
            return;
        }

        _popoutImage = new System.Windows.Controls.Image { Stretch = Stretch.UniformToFill };
        _popoutBlackOverlay = new System.Windows.Shapes.Rectangle
        {
            Fill = _selectedColorBrush,
            Visibility = _isBlackFrame ? Visibility.Visible : Visibility.Collapsed
        };

        var grid = new Grid { Background = new SolidColorBrush(MediaColor.FromRgb(0x14, 0x15, 0x18)) };
        grid.Children.Add(_popoutImage);
        grid.Children.Add(_popoutBlackOverlay);

        _camPopoutWindow = new Window
        {
            Title = "CastDecoy - Visor Flotante de Cámara",
            Width = 380,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(MediaColor.FromRgb(0x14, 0x15, 0x18)),
            Topmost = true,
            Content = grid
        };

        if (_lastFrameBytes != null)
        {
            DisplayFrame(_lastFrameBytes);
        }

        _camPopoutWindow.Closed += (_, _) =>
        {
            _popoutImage = null;
            _popoutBlackOverlay = null;
            _camPopoutWindow = null;
        };

        _camPopoutWindow.Show();
    }

    
    private void CleanupCamera()
    {
        StopLoopPlayback();
        try
        {
            _camCts?.Cancel();
            if (_activeCaptureDevice != null)
            {
                var dev = _activeCaptureDevice;
                _activeCaptureDevice = null;
                try { dev.StopAsync().Wait(1000); } catch { }
                try { dev.Dispose(); } catch { }
            }
        }
        catch { }
    }

        private void OnFreezeCamToggleClick(object sender, RoutedEventArgs e)
    {
        ToggleFreezeCamera();
    }

    
    private void OnLoopToggleClick(object sender, RoutedEventArgs e)
    {
        if (_isLooping)
        {
            StopLoopPlayback();
            ModeLiveRadio.IsChecked = true;
            SetLiveMode();
        }
        else
        {
            ModeLoopRadio.IsChecked = true;
            SetLoopMode(true);
        }
    }

    
    private void OnStutterOptionClick(object? sender, RoutedEventArgs? e)
    {
        ModeStutterRadio.IsChecked = true;
        OnCamModeRadioClicked(ModeStutterRadio, null!);
    }

    
    private void OnToggleStutterClick(object sender, RoutedEventArgs e)
    {
        if (_isCamStuttering)
        {
            StopStutterMode();
            ModeLiveRadio.IsChecked = true;
            SetLiveMode();
        }
        else
        {
            ModeStutterRadio.IsChecked = true;
            SetStutterMode(true);
        }
    }

    
    private void OnStutterCycleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StutterCycleCombo == null) return;
        if (StutterCycleCombo.SelectedIndex == 0) _stutterCycleSeconds = 10;
        else if (StutterCycleCombo.SelectedIndex == 1) _stutterCycleSeconds = 15;
        else if (StutterCycleCombo.SelectedIndex == 2) _stutterCycleSeconds = 30;
        else _stutterCycleSeconds = 9999;
    }

    
    private async void SetStutterMode(bool active)
    {
        ClearAllCamEffects();

        if (active)
        {
            if (!_isCamActive)
            {
                await ActivateCameraAsync();
            }

            _isCamStuttering = true;
            ModeStutterRadio.IsChecked = true;
            _stutterStopwatch.Restart();

            StutterCamBtn.Content = "Detener";
            StutterCamBtn.Style = (Style)FindResource("PrimaryPillBtn");

            CamBadgeText.Text = "Trabado / Lag";
            CamStatusDot.Fill = AccentBlueBrush;
            TileCamStatusText.Text = "Señal inestable / Lag";
            TileCamStatusText.Foreground = AccentBlueBrush;
            StutterBadge.Visibility = Visibility.Visible;
        }
        else
        {
            StopStutterMode();
            ModeLiveRadio.IsChecked = true;
            SetLiveMode();
        }
    }

    
    private void StopStutterMode()
    {
        _isCamStuttering = false;
        _stutterStopwatch.Stop();
        if (StutterBadge != null) StutterBadge.Visibility = Visibility.Collapsed;
        if (StutterCamBtn != null)
        {
            StutterCamBtn.Content = "Iniciar";
            StutterCamBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
    }

    
    private void OnGlitchOptionClick(object sender, MouseButtonEventArgs e)
    {
        ModeGlitchRadio.IsChecked = true;
        OnCamModeRadioClicked(ModeGlitchRadio, null!);
    }

    
    private void OnToggleGlitchClick(object sender, RoutedEventArgs e)
    {
        ModeGlitchRadio.IsChecked = true;
        OnCamModeRadioClicked(ModeGlitchRadio, null!);
    }

    
    private void OnGlitchModeChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    
    private void SetGlitchMode(bool active)
    {
        ClearAllCamEffects();
        _isGlitchActive = active;
        if (_isGlitchActive)
        {
            ModeGlitchRadio.IsChecked = true;
            if (GlitchToggleBtn != null) GlitchToggleBtn.Style = (Style)FindResource("PrimaryPillBtn");
            if (GlitchFrameOverlay != null) GlitchFrameOverlay.Visibility = Visibility.Visible;
            if (_isCamActive && CamBadgeText != null)
            {
                CamBadgeText.Text = "Video cortado";
                CamStatusDot.Fill = StatusAmberBrush;
            }
        }
    }

}
