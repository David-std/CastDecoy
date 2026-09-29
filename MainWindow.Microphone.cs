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
    private readonly Border[] _inBars = new Border[12];
    private readonly Border[] _outBars = new Border[12];
    private DispatcherTimer? _spectrumTimer;
    private float _currentMicPeak = 0f;
    private int _spectrumTick = 0;

    // Relative human vocal frequency distribution across 12 bands (60Hz to 16kHz)
    private static readonly double[] VocalFrequencies = { 0.22, 0.48, 0.85, 0.95, 0.90, 0.78, 0.82, 0.68, 0.58, 0.48, 0.38, 0.28 };

    private void InitMicrophoneControls()
    {
        _audioService = new AudioSpoofService();
        _audioService.PeakLevelChanged += OnMicPeakLevelChanged;
        ScanMicDevices();
        SetupSpectrumBars();

        if (EchoDelayCombo != null && EchoDelayCombo.SelectedIndex >= 0)
            _audioService.SetEchoMode(EchoDelayCombo.SelectedIndex);
        if (StaticTypeCombo != null && StaticTypeCombo.SelectedIndex >= 0)
            _audioService.SetStaticType(StaticTypeCombo.SelectedIndex);
        if (ChoppyRateCombo != null && ChoppyRateCombo.SelectedIndex >= 0)
            _audioService.SetChoppyRate(ChoppyRateCombo.SelectedIndex);
        if (MicMasterVolumeSlider != null)
            _audioService.SetMasterVolume((float)(MicMasterVolumeSlider.Value / 100.0));
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
            if (ChoppyRateCombo != null && ChoppyRateCombo.SelectedIndex >= 0)
                _audioService.SetChoppyRate(ChoppyRateCombo.SelectedIndex);
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
            if (EchoDelayCombo != null && EchoDelayCombo.SelectedIndex >= 0)
                _audioService.SetEchoMode(EchoDelayCombo.SelectedIndex);
            MicStatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(0xfb, 0xbc, 0x04));
            MicStatusNotice.Text = "Eco doble y reverberación activos";
            TileMicStatusText.Text = "Eco doble activo";
            TileMicStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xfb, 0xbc, 0x04));
            ToggleFakeMuteBtn.Content = "Silencio falso";
            ToggleFakeMuteBtn.Style = (Style)FindResource("SecondaryPillBtn");
        }
        else if (ModeMicStaticRadio.IsChecked == true)
        {
            _audioService.SetMode(AudioSpoofMode.StaticNoise);
            if (StaticTypeCombo != null && StaticTypeCombo.SelectedIndex >= 0)
                _audioService.SetStaticType(StaticTypeCombo.SelectedIndex);
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

    private void OnMicMasterVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MicMasterVolumeText == null) return;
        int val = (int)e.NewValue;
        MicMasterVolumeText.Text = $"{val}%";
        _audioService?.SetMasterVolume(val / 100f);
    }

    private void OnEchoDelayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_audioService == null || EchoDelayCombo == null) return;
        _audioService.SetEchoMode(EchoDelayCombo.SelectedIndex);
    }

    private void OnStaticTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_audioService == null || StaticTypeCombo == null) return;
        _audioService.SetStaticType(StaticTypeCombo.SelectedIndex);
    }

    private void OnChoppyRateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_audioService == null || ChoppyRateCombo == null) return;
        _audioService.SetChoppyRate(ChoppyRateCombo.SelectedIndex);
    }

    private void SetupSpectrumBars()
    {
        if (InSpectrumGrid == null || OutSpectrumGrid == null) return;
        InSpectrumGrid.Children.Clear();
        OutSpectrumGrid.Children.Clear();

        for (int i = 0; i < 12; i++)
        {
            var bIn = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(0x5b, 0x8d, 0xf7)),
                CornerRadius = new CornerRadius(2.5, 2.5, 0, 0),
                Width = 9.5,
                Margin = new Thickness(2, 0, 2, 0),
                Height = 3,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            _inBars[i] = bIn;
            InSpectrumGrid.Children.Add(bIn);

            var bOut = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(0x5b, 0x8d, 0xf7)),
                CornerRadius = new CornerRadius(2.5, 2.5, 0, 0),
                Width = 9.5,
                Margin = new Thickness(2, 0, 2, 0),
                Height = 3,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            _outBars[i] = bOut;
            OutSpectrumGrid.Children.Add(bOut);
        }

        _spectrumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _spectrumTimer.Tick += OnSpectrumTimerTick;
        _spectrumTimer.Start();
    }

    private void OnSpectrumTimerTick(object? sender, EventArgs e)
    {
        _spectrumTick++;
        if (!_isMicActive)
        {
            for (int i = 0; i < 12; i++)
            {
                _inBars[i].Height = Math.Max(2, _inBars[i].Height * 0.85);
                _outBars[i].Height = Math.Max(2, _outBars[i].Height * 0.85);
            }
            return;
        }

        float p = Math.Clamp(_currentMicPeak, 0.04f, 1f);
        float masterVol = _audioService?.MasterVolume ?? 1f;
        double maxH = 54.0;

        for (int i = 0; i < 12; i++)
        {
            // Human voice formant energy distribution with subtle dynamic organic jitter
            double jitter = Math.Sin(_spectrumTick * 0.42 + i * 1.35) * 0.12 + Math.Cos(_spectrumTick * 0.28 + i * 0.95) * 0.08;
            double bandEnvelope = Math.Clamp(VocalFrequencies[i] * p + jitter * p, 0.05, 1.0);
            double targetIn = Math.Clamp(bandEnvelope * maxH, 3.0, maxH);
            _inBars[i].Height = _inBars[i].Height * 0.45 + targetIn * 0.55;

            double targetOut = targetIn;
            if (ModeMicFakeMuteRadio.IsChecked == true)
            {
                targetOut = 2.0;
                MicEffectBadge.Text = "MUTE";
            }
            else if (ModeMicChoppyRadio.IsChecked == true)
            {
                int cycleLength = ChoppyRateCombo.SelectedIndex == 2 ? 16 : (ChoppyRateCombo.SelectedIndex == 0 ? 30 : 22);
                int muteLength = (int)(cycleLength * (ChoppyRateCombo.SelectedIndex == 2 ? 0.75 : (ChoppyRateCombo.SelectedIndex == 0 ? 0.25 : 0.50)));
                bool isChopped = (_spectrumTick % cycleLength) < muteLength;
                targetOut = isChopped ? 2.0 : targetIn;
                MicEffectBadge.Text = "ENTRECORTADO";
            }
            else if (ModeMicEchoRadio.IsChecked == true)
            {
                if (EchoDelayCombo.SelectedIndex == 0)
                {
                    // Eco doble y largo (pulsos y cola amplia)
                    double echoBounce = (Math.Sin(_spectrumTick * 0.22) > 0.35) ? 1.4 : 0.7;
                    targetOut = Math.Clamp(targetIn * echoBounce, 4.0, maxH);
                    MicEffectBadge.Text = "ECO DOBLE LARGO";
                }
                else
                {
                    targetOut = Math.Clamp(targetIn * 1.25, 4.0, maxH);
                    MicEffectBadge.Text = "ECO MÚLTIPLE";
                }
            }
            else if (ModeMicStaticRadio.IsChecked == true)
            {
                if (StaticTypeCombo.SelectedIndex == 0)
                {
                    // Zumbido eléctrico 50-60 Hz: picos en frecuencias bajas (bandas 0 y 1)
                    double humEnergy = i <= 1 ? (0.85 + Math.Sin(_spectrumTick * 0.8) * 0.1) : 0.15;
                    targetOut = Math.Clamp((targetIn * 0.35 + humEnergy) * maxH, 5.0, maxH);
                    MicEffectBadge.Text = "ZUMBIDO 50-60HZ";
                }
                else if (StaticTypeCombo.SelectedIndex == 1)
                {
                    // Ruido blanco continuo: uniforme en todas las 12 bandas
                    double whiteEnergy = 0.55 + Math.Sin(_spectrumTick * 1.5 + i * 2.1) * 0.2;
                    targetOut = Math.Clamp((targetIn * 0.30 + whiteEnergy) * maxH, 6.0, maxH);
                    MicEffectBadge.Text = "RUIDO BLANCO";
                }
                else
                {
                    // Estática sibilante de cable: picos en agudos (bandas 7 a 11)
                    double hissEnergy = i >= 7 ? (0.75 + Math.Sin(_spectrumTick * 2.3 + i * 1.8) * 0.25) : 0.18;
                    targetOut = Math.Clamp((targetIn * 0.35 + hissEnergy) * maxH, 5.0, maxH);
                    MicEffectBadge.Text = "ESTÁTICA CABLE";
                }
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

            // Aplicar el volumen general de salida a la visualización de salida
            double finalOut = targetOut * masterVol;
            _outBars[i].Height = _outBars[i].Height * 0.45 + finalOut * 0.55;
        }
    }

    private void OnChoppyOptionClick(object? sender, RoutedEventArgs? e) => OnMicChoppyOptionClick(sender!, null!);
}
