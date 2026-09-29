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
    private AudioSpoofService? _audioService;
    private bool _isMicActive = false;
    private readonly Border[] _inBars = new Border[16];
    private readonly Border[] _outBars = new Border[16];
    private DispatcherTimer? _spectrumTimer;
    private float _currentMicPeak = 0f;
    private int _spectrumTick = 0;

    
    private void InitMicrophoneControls()
    {
        _audioService = new AudioSpoofService();
        _audioService.PeakLevelChanged += OnMicPeakLevelChanged;
        ScanMicDevices();
        SetupSpectrumBars();
    }

    
    private void ScanMicDevices()
    {
        MicDeviceComboBox.Items.Clear();
        var mics = AudioSpoofService.GetInputDevices();
        foreach (var m in mics) MicDeviceComboBox.Items.Add(m);
        if (MicDeviceComboBox.Items.Count == 0)
        {
            MicDeviceComboBox.Items.Add("Micrófono predeterminado (Realtek Audio)");
            MicDeviceComboBox.Items.Add("CABLE Output (VB-Audio Virtual Cable)");
        }
        MicDeviceComboBox.SelectedIndex = 0;
    }

    
    private void OnRefreshMicsClick(object sender, RoutedEventArgs e)
    {
        ScanMicDevices();
    }

    
    private void OnMicDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isMicActive)
        {
            ActivateMic();
        }
    }

    
    private void OnMicPeakLevelChanged(float peak)
    {
        _currentMicPeak = peak;
        Dispatcher.InvokeAsync(() =>
        {
            if (!_isMicActive)
            {
                if (MicVuMeter != null) MicVuMeter.Value = 0;
                if (MicVolumeText != null) MicVolumeText.Text = "0%";
                if (MicDbLevelText != null) MicDbLevelText.Text = "-inf dB";
                UpdateVuBlocks(0);
                return;
            }
            int pct = (int)(peak * 100);
            if (MicVuMeter != null) MicVuMeter.Value = pct;
            if (MicVolumeText != null) MicVolumeText.Text = $"{pct}%";
            if (MicDbLevelText != null)
            {
                float db = peak > 0.001f ? (float)(20 * Math.Log10(peak)) : -60f;
                MicDbLevelText.Text = $"{db:0.0} dB";
            }
            UpdateVuBlocks(pct);
        }, DispatcherPriority.Background);
    }

    
    private void UpdateVuBlocks(int pct)
    {
        if (MicVuBlocksPanel == null) return;
        int total = MicVuBlocksPanel.Children.Count;
        int lit = (int)Math.Round((pct / 100.0) * total);
        for (int i = 0; i < total; i++)
        {
            if (MicVuBlocksPanel.Children[i] is Border b)
            {
                if (i < lit)
                {
                    if (i >= total - 2)
                        b.Background = new SolidColorBrush(MediaColor.FromRgb(0xff, 0x3b, 0x30)); // Red clip
                    else if (i >= total - 5)
                        b.Background = new SolidColorBrush(MediaColor.FromRgb(0xff, 0x95, 0x00)); // Amber warning
                    else
                        b.Background = new SolidColorBrush(MediaColor.FromRgb(0x5b, 0x8d, 0xf7)); // Brand blue
                }
                else
                {
                    b.Background = new SolidColorBrush(MediaColor.FromRgb(0xe5, 0xe5, 0xea)); // Unlit
                }
            }
        }
    }

    
    private void OnActivateMicClick(object sender, RoutedEventArgs e)
    {
        if (_isMicActive)
        {
            DeactivateMic();
        }
        else
        {
            ActivateMic();
        }
    }

    
    private void ActivateMic()
    {
        if (MicDeviceComboBox.Items.Count == 0)
        {
            ScanMicDevices();
        }

        if (MicDeviceComboBox.Items.Count == 0)
        {
            MessageBox.Show("No se detectó ningún dispositivo de micrófono disponible.", "CastDecoy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            int devIndex = MicDeviceComboBox.SelectedIndex >= 0 ? MicDeviceComboBox.SelectedIndex : 0;
            _audioService?.Start(devIndex);
            _isMicActive = true;

            ActivateMicBtn.Content = "Detener micrófono";
            ActivateMicBtn.Style = (Style)FindResource("SecondaryPillBtn");

            ToggleFakeMuteBtn.IsEnabled = true;
            ToggleFakeMuteBtn.IsEnabled = true;

            ApplyActiveMicMode();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al inicializar micrófono: {ex.Message}", "CastDecoy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    
    private void DeactivateMic()
    {
        _isMicActive = false;
        _audioService?.Stop();

        ActivateMicBtn.Content = "Activar micrófono";
        ActivateMicBtn.Style = (Style)FindResource("PrimaryPillBtn");

        ToggleFakeMuteBtn.IsEnabled = false;
        ToggleFakeMuteBtn.IsEnabled = false;

        MicBadgeText.Text = "Inactivo";
        MicBadgeText.Foreground = InactiveTextBrush;
        MicStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0x5f, 0x63, 0x68));
        MicStatusNotice.Text = "Micrófono apagado";
        TileMicStatusText.Text = "Micrófono inactivo";
        TileMicStatusText.Foreground = InactiveTextBrush;

        MicVuMeter.Value = 0;
        MicVolumeText.Text = "0%";
        if (MicDbLevelText != null) MicDbLevelText.Text = "-inf dB";
        UpdateVuBlocks(0);
    }

    
        private void ApplyActiveMicMode()
    {
        if (_audioService == null) return;

        if (ModeMicFakeMuteRadio.IsChecked == true)
        {
            _audioService.SetMode(AudioSpoofMode.FakeMute);
            MicStatusDot.Fill = StatusRedBrush;
            MicStatusNotice.Text = "Silencio simulado activo (0 dB)";
            TileMicStatusText.Text = "Mute falso activo";
            TileMicStatusText.Foreground = StatusRedBrush;
            ToggleFakeMuteBtn.Content = "Reanudar audio";
            ToggleFakeMuteBtn.Style = (Style)FindResource("PrimaryPillBtn");
        }
        else if (ModeMicChoppyRadio.IsChecked == true)
        {
            _audioService.SetMode(AudioSpoofMode.Choppy);
            MicStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0xfb, 0xbc, 0x04));
            MicStatusNotice.Text = "Voz entrecortada activa";
            TileMicStatusText.Text = "Voz entrecortada";
            TileMicStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xfb, 0xbc, 0x04));
            ToggleFakeMuteBtn.Content = "Silencio falso";
            ToggleFakeMuteBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
        else if (ModeMicEchoRadio.IsChecked == true)
        {
            _audioService.SetMode(AudioSpoofMode.Echo);
            MicStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0xfb, 0xbc, 0x04));
            MicStatusNotice.Text = "Eco y retorno doble activos";
            TileMicStatusText.Text = "Eco doble activo";
            TileMicStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xfb, 0xbc, 0x04));
            ToggleFakeMuteBtn.Content = "Silencio falso";
            ToggleFakeMuteBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
        else if (ModeMicStaticRadio.IsChecked == true)
        {
            _audioService.SetMode(AudioSpoofMode.StaticNoise);
            MicStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0xfb, 0xbc, 0x04));
            MicStatusNotice.Text = "Ruido de estática activo";
            TileMicStatusText.Text = "Estática activa";
            TileMicStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xfb, 0xbc, 0x04));
            ToggleFakeMuteBtn.Content = "Silencio falso";
            ToggleFakeMuteBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
        else if (ModeMicSaturationRadio.IsChecked == true)
        {
            _audioService.SetMode(AudioSpoofMode.Saturation);
            MicStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0xfb, 0xbc, 0x04));
            MicStatusNotice.Text = $"Saturación x{(int)MicSaturationSlider.Value} activa";
            TileMicStatusText.Text = "Saturación activa";
            TileMicStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xfb, 0xbc, 0x04));
            ToggleFakeMuteBtn.Content = "Silencio falso";
            ToggleFakeMuteBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
        else
        {
            _audioService.SetMode(AudioSpoofMode.Normal);
            MicStatusDot.Fill = StatusGreenBrush;
            MicStatusNotice.Text = "Audio limpio en directo";
            TileMicStatusText.Text = "Micrófono en vivo";
            TileMicStatusText.Foreground = InactiveTextBrush;
            ToggleFakeMuteBtn.Content = "Silencio falso";
            ToggleFakeMuteBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
    }

    
    private void OnMicNormalOptionClick(object sender, MouseButtonEventArgs e)
    {
        ModeMicNormalRadio.IsChecked = true;
        ApplyActiveMicMode();
    }

    
    private void OnMicFakeMuteOptionClick(object sender, MouseButtonEventArgs e)
    {
        ModeMicFakeMuteRadio.IsChecked = true;
        ApplyActiveMicMode();
    }

    
    private void OnMicChoppyOptionClick(object sender, MouseButtonEventArgs e)
    {
        ModeMicChoppyRadio.IsChecked = true;
        ApplyActiveMicMode();
    }

    
    private void OnMicEchoOptionClick(object sender, MouseButtonEventArgs e)
    {
        ModeMicEchoRadio.IsChecked = true;
        ApplyActiveMicMode();
    }

    
    private void OnMicStaticOptionClick(object sender, MouseButtonEventArgs e)
    {
        ModeMicStaticRadio.IsChecked = true;
        ApplyActiveMicMode();
    }

    
    private void OnMicSaturationOptionClick(object sender, MouseButtonEventArgs e)
    {
        ModeMicSaturationRadio.IsChecked = true;
        ApplyActiveMicMode();
    }

    
    private void OnMicModeRadioClicked(object sender, RoutedEventArgs e)
    {
        ApplyActiveMicMode();
    }

    
    private void OnToggleFakeMuteClick(object sender, RoutedEventArgs e)
    {
        ToggleMicFakeMute();
    }

    
    private void ToggleMicFakeMute()
    {
        if (!_isMicActive)
        {
            ActivateMic();
            ModeMicFakeMuteRadio.IsChecked = true;
            ApplyActiveMicMode();
            return;
        }

        if (ModeMicFakeMuteRadio.IsChecked == true)
        {
            ModeMicNormalRadio.IsChecked = true;
            ApplyActiveMicMode();
        }
        else
        {
            ModeMicFakeMuteRadio.IsChecked = true;
            ApplyActiveMicMode();
        }
    }

    
    private void OnMonitorAudioChecked(object sender, RoutedEventArgs e)
    {
        _audioService?.SetMonitoring(MonitorAudioCheckBox.IsChecked == true);
    }

    
    private void OnMicSaturationSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SaturationLevelText == null) return;
        int val = (int)e.NewValue;
        string desc = val < 8 ? "Baja" : (val < 18 ? "Alta" : "Extrema");
        SaturationLevelText.Text = $"x{val} ({desc})";
        _audioService?.SetSaturationGain(val);
    }

    
    private void SetupSpectrumBars()
    {
        if (InSpectrumGrid == null || OutSpectrumGrid == null) return;
        InSpectrumGrid.Children.Clear();
        OutSpectrumGrid.Children.Clear();
        for (int i = 0; i < 16; i++)
        {
            var bIn = new Border
            {
                Background = AccentBlueBrush,
                CornerRadius = new CornerRadius(1.5),
                Margin = new Thickness(1.5, 0, 1.5, 0),
                Height = 3,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            _inBars[i] = bIn;
            InSpectrumGrid.Children.Add(bIn);

            var bOut = new Border
            {
                Background = AccentBlueBrush,
                CornerRadius = new CornerRadius(1.5),
                Margin = new Thickness(1.5, 0, 1.5, 0),
                Height = 3,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            _outBars[i] = bOut;
            OutSpectrumGrid.Children.Add(bOut);
        }

        _spectrumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _spectrumTimer.Tick += OnSpectrumTimerTick;
        _spectrumTimer.Start();
    }

    
    private void OnSpectrumTimerTick(object? sender, EventArgs e)
    {
        _spectrumTick++;
        if (!_isMicActive)
        {
            for (int i = 0; i < 16; i++)
            {
                _inBars[i].Height = Math.Max(2, _inBars[i].Height * 0.85);
                _outBars[i].Height = Math.Max(2, _outBars[i].Height * 0.85);
            }
            return;
        }

        float p = Math.Clamp(_currentMicPeak, 0.05f, 1f);
        double maxH = 58.0;

        for (int i = 0; i < 16; i++)
        {
            double freqCurve = Math.Sin((i + 1) * Math.PI / 18.0);
            double noise = ((Math.Sin(_spectrumTick * 0.3 + i * 1.2) + 1.0) / 2.0) * 0.35;
            double targetIn = Math.Clamp((freqCurve * p * 0.8 + noise * p) * maxH, 3.0, maxH);
            _inBars[i].Height = _inBars[i].Height * 0.5 + targetIn * 0.5;

            double targetOut = targetIn;
            if (ModeMicFakeMuteRadio.IsChecked == true)
            {
                targetOut = 2.0;
                MicEffectBadge.Text = "MUTE";
            }
            else if (ModeMicChoppyRadio.IsChecked == true)
            {
                bool isChopped = (_spectrumTick % 20) < 10;
                targetOut = isChopped ? 2.0 : targetIn;
                MicEffectBadge.Text = "CHOPPY / LAG";
            }
            else if (ModeMicEchoRadio.IsChecked == true)
            {
                targetOut = Math.Clamp(targetIn * 1.2, 4.0, maxH);
                MicEffectBadge.Text = "ECO DOBLE";
            }
            else if (ModeMicStaticRadio.IsChecked == true)
            {
                double staticNoise = ((Math.Sin(_spectrumTick * 1.7 + i * 2.3) + 1.0) / 2.0) * 0.65;
                targetOut = Math.Clamp((targetIn * 0.4 + staticNoise) * maxH, 6.0, maxH);
                MicEffectBadge.Text = "ESTÁTICA";
            }
            else if (ModeMicSaturationRadio.IsChecked == true)
            {
                targetOut = maxH;
                MicEffectBadge.Text = "SATURADO";
            }
            else
            {
                MicEffectBadge.Text = "EN VIVO";
            }

            _outBars[i].Height = _outBars[i].Height * 0.5 + targetOut * 0.5;
        }
    }

    
    private void OnChoppyOptionClick(object? sender, RoutedEventArgs? e) => OnMicChoppyOptionClick(sender!, null!);

}
