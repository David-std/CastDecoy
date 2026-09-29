using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace CastDecoy;

public class HotkeyBinding
{
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public uint VirtualKey { get; set; }

    public bool IsAssigned => VirtualKey != 0;

    public string DisplayText
    {
        get
        {
            if (!IsAssigned) return "Ctrl + Alt + ...";
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");

            string keyName = KeyInterop.KeyFromVirtualKey((int)VirtualKey).ToString();
            if (VirtualKey >= 0x30 && VirtualKey <= 0x39)
                keyName = ((char)('0' + (VirtualKey - 0x30))).ToString();
            else if (VirtualKey >= 0x70 && VirtualKey <= 0x87)
                keyName = "F" + (VirtualKey - 0x6F);
            else if (keyName.StartsWith("D") && keyName.Length == 2 && char.IsDigit(keyName[1]))
                keyName = keyName.Substring(1);

            parts.Add(keyName);
            return string.Join(" + ", parts);
        }
    }

    public bool IsMatch(bool ctrl, bool alt, bool shift, uint vk)
    {
        if (!IsAssigned) return false;
        return Ctrl == ctrl && Alt == alt && Shift == shift && VirtualKey == vk;
    }

    public HotkeyBinding Clone() => new()
    {
        Ctrl = Ctrl,
        Alt = Alt,
        Shift = Shift,
        VirtualKey = VirtualKey
    };
}
