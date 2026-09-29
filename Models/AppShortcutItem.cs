using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace CastDecoy;

public class AppShortcutItem : INotifyPropertyChanged
{
    private HotkeyBinding _hotkey = new();
    private string _title = string.Empty;

    private ImageSource? _icon;
    private Brush _iconBackground = new SolidColorBrush(Color.FromRgb(0xf2, 0xf2, 0xf7));
    private Brush _iconBorderBrush = new SolidColorBrush(Color.FromRgb(0xe5, 0xe5, 0xea));

    public IntPtr Handle { get; set; }
    public uint ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CleanTitle));
        }
    }

    public string CleanTitle => WindowInfo.FormatTitle(Title, ProcessName);

    public Brush? IconBackground
    {
        get => _iconBackground;
        set
        {
            if (value != null) _iconBackground = value;
            OnPropertyChanged();
        }
    }

    public Brush? IconBorderBrush
    {
        get => _iconBorderBrush;
        set
        {
            if (value != null) _iconBorderBrush = value;
            OnPropertyChanged();
        }
    }

    public void UpdateIconContrast()
    {
        var (bg, border) = CastDecoy.Services.IconContrastHelper.GetAdaptiveBrushes(_icon);
        IconBackground = bg;
        IconBorderBrush = border;
    }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            OnPropertyChanged();
            UpdateIconContrast();
        }
    }

    public HotkeyBinding Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            NotifyHotkeyChanged();
        }
    }

    public string HotkeyDisplay => Hotkey.DisplayText;
    public string HotkeyText => Hotkey.DisplayText;
    public bool IsAssigned => Hotkey.IsAssigned;

    public SolidColorBrush ChipBackground => Brushes.Transparent;
    public SolidColorBrush ChipBorderBrush => Brushes.Transparent;

    public SolidColorBrush StatusColor => IsAssigned 
        ? new SolidColorBrush(Color.FromRgb(0x8a, 0xb4, 0xf8)) 
        : new SolidColorBrush(Color.FromRgb(0x70, 0x75, 0x7a));

    public SolidColorBrush HotkeyColorBrush => StatusColor;

    public void NotifyHotkeyChanged()
    {
        OnPropertyChanged(nameof(HotkeyDisplay));
        OnPropertyChanged(nameof(HotkeyText));
        OnPropertyChanged(nameof(IsAssigned));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(HotkeyColorBrush));
        OnPropertyChanged(nameof(ChipBackground));
        OnPropertyChanged(nameof(ChipBorderBrush));
    }

    public void UpdateVisuals() => NotifyHotkeyChanged();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
