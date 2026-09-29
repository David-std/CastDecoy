using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CastDecoy;

public class WindowInfo : INotifyPropertyChanged
{
    private bool _isGhosted;
    private string _statusText = "Visible";
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private ImageSource? _icon;
    private BitmapSource? _thumbnail;
    private bool _isSelected;

    private string _title = string.Empty;
    private int _windowWidth;
    private int _windowHeight;
    private bool _isMinimized;

    public IntPtr Handle { get; init; }
    public string Title
    {
        get => _title;
        set
        {
            if (SetField(ref _title, value))
            {
                OnPropertyChanged(nameof(CleanTitle));
            }
        }
    }
    public string ProcessName { get; init; } = string.Empty;
    public uint ProcessId { get; init; }
    public bool IsSelf { get; init; }
    public int WindowWidth
    {
        get => _windowWidth;
        set => SetField(ref _windowWidth, value);
    }
    public int WindowHeight
    {
        get => _windowHeight;
        set => SetField(ref _windowHeight, value);
    }
    public bool IsMinimized
    {
        get => _isMinimized;
        set => SetField(ref _isMinimized, value);
    }

    public string CleanTitle => FormatTitle(Title, ProcessName);

    public static string FormatTitle(string title, string processName)
    {
        if (string.IsNullOrWhiteSpace(title)) return processName;
        string text = title.Trim();

        if (text.Contains('\\'))
        {
            int dashIdx = text.IndexOf(" - ", StringComparison.Ordinal);
            string part = dashIdx > 0 ? text.Substring(0, dashIdx).Trim() : text;
            int tabIdx = part.IndexOf(" y ", StringComparison.OrdinalIgnoreCase);
            if (tabIdx > 0) part = part.Substring(0, tabIdx).Trim();
            try
            {
                string seg = Path.GetFileName(part.TrimEnd('\\'));
                if (!string.IsNullOrWhiteSpace(seg)) text = seg;
            }
            catch { }
        }
        else if (text.Contains(" - "))
        {
            string[] parts = text.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2)
            {
                text = parts[0].Trim();
            }
        }

        if (text.Length > 28)
        {
            text = text.Substring(0, 26) + "...";
        }

        return text;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    private Brush _iconBackground = new SolidColorBrush(Color.FromRgb(0xf2, 0xf2, 0xf7));
    private Brush _iconBorderBrush = new SolidColorBrush(Color.FromRgb(0xe5, 0xe5, 0xea));

    public Brush IconBackground
    {
        get => _iconBackground;
        set => SetField(ref _iconBackground, value);
    }

    public Brush IconBorderBrush
    {
        get => _iconBorderBrush;
        set => SetField(ref _iconBorderBrush, value);
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
            if (SetField(ref _icon, value))
            {
                UpdateIconContrast();
            }
        }
    }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set => SetField(ref _thumbnail, value);
    }

    public bool IsGhosted
    {
        get => _isGhosted;
        set => SetField(ref _isGhosted, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetField(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
