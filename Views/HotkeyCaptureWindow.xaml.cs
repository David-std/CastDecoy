using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CastDecoy;

public partial class HotkeyCaptureWindow : Window
{
    public HotkeyBinding ResultBinding { get; private set; }

    public HotkeyCaptureWindow(string targetTitle, HotkeyBinding currentBinding)
    {
        InitializeComponent();
        DialogTitleText.Text = $"Atajo para: {targetTitle}";
        ResultBinding = currentBinding.Clone();
        MainWindow.IsCapturingHotkey = true;
        Closed += (_, _) => MainWindow.IsCapturingHotkey = false;
        UpdatePreview();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Enter)
        {
            DialogResult = true;
            Close();
            return;
        }

        if (key == Key.Escape)
        {
            DialogResult = false;
            Close();
            return;
        }

        if ((key == Key.Delete || key == Key.Back) && Keyboard.Modifiers == ModifierKeys.None)
        {
            OnClearClick(sender, e);
            return;
        }

        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin)
        {
            bool mCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool mAlt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            bool mShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var parts = new List<string>();
            if (mCtrl) parts.Add("Ctrl");
            if (mAlt) parts.Add("Alt");
            if (mShift) parts.Add("Shift");
            parts.Add("...");
            LivePreviewText.Text = string.Join(" + ", parts);
            LivePreviewText.Foreground = new SolidColorBrush(Color.FromRgb(0x5b, 0x8d, 0xf7));
            return;
        }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        ResultBinding.Ctrl = ctrl;
        ResultBinding.Alt = alt;
        ResultBinding.Shift = shift;
        ResultBinding.VirtualKey = vk;

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        LivePreviewText.Text = ResultBinding.DisplayText;
        LivePreviewText.Foreground = ResultBinding.IsAssigned 
            ? new SolidColorBrush(Color.FromRgb(0x5b, 0x8d, 0xf7))
            : new SolidColorBrush(Color.FromRgb(0x6e, 0x6e, 0x73));
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ResultBinding.Ctrl = false;
        ResultBinding.Alt = false;
        ResultBinding.Shift = false;
        ResultBinding.VirtualKey = 0;
        DialogResult = true;
        Close();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
