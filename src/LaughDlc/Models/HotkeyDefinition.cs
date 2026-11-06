using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace LaughDlc.Models;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    All = Alt | Control | Shift | Win
}

public sealed class HotkeyDefinition : IEquatable<HotkeyDefinition>
{
    public HotkeyModifiers Modifiers { get; set; }

    public Keys Key { get; set; }

    [JsonIgnore]
    public bool IsValid => IsValidCombination(Modifiers, Key);

    [JsonIgnore]
    public string DisplayText => Describe(Modifiers, Key);

    public HotkeyDefinition()
    {
    }

    public HotkeyDefinition(HotkeyModifiers modifiers, Keys key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public static HotkeyDefinition CreateDefault()
    {
        return new HotkeyDefinition(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.L);
    }

    public HotkeyDefinition Clone()
    {
        return new HotkeyDefinition(Modifiers, Key);
    }

    public bool Equals(HotkeyDefinition? other)
    {
        return other is not null && Modifiers == other.Modifiers && Key == other.Key;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as HotkeyDefinition);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Modifiers, Key);
    }

    public override string ToString()
    {
        return DisplayText;
    }

    public static bool IsValidCombination(HotkeyModifiers modifiers, Keys key)
    {
        var knownModifiers = modifiers & ~HotkeyModifiers.All;
        if (knownModifiers != HotkeyModifiers.None)
        {
            return false;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        if (key == Keys.None || IsModifierKey(key))
        {
            return false;
        }

        if ((key & Keys.Modifiers) != Keys.None)
        {
            return false;
        }

        return true;
    }

    public static bool IsModifierKey(Keys key)
    {
        return key is Keys.None
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu
            or Keys.LWin or Keys.RWin;
    }

    public static string Describe(HotkeyModifiers modifiers, Keys key)
    {
        var parts = new List<string>(5);

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(GetKeyDisplayName(key));
        return string.Join(" + ", parts);
    }

    public static string GetKeyDisplayName(Keys key)
    {
        var keyCode = key & Keys.KeyCode;

        if (keyCode >= Keys.A && keyCode <= Keys.Z)
        {
            return ((char)('A' + (keyCode - Keys.A))).ToString();
        }

        if (keyCode >= Keys.D0 && keyCode <= Keys.D9)
        {
            return ((char)('0' + (keyCode - Keys.D0))).ToString();
        }

        if (keyCode >= Keys.NumPad0 && keyCode <= Keys.NumPad9)
        {
            return "Num " + (keyCode - Keys.NumPad0);
        }

        if (keyCode >= Keys.F1 && keyCode <= Keys.F24)
        {
            return "F" + (keyCode - Keys.F1 + 1);
        }

        return keyCode switch
        {
            Keys.Back => "Backspace",
            Keys.Tab => "Tab",
            Keys.Enter => "Enter",
            Keys.Escape => "Esc",
            Keys.Space => "Space",
            Keys.PageUp => "Page Up",
            Keys.PageDown => "Page Down",
            Keys.End => "End",
            Keys.Home => "Home",
            Keys.Left => "Left",
            Keys.Up => "Up",
            Keys.Right => "Right",
            Keys.Down => "Down",
            Keys.Insert => "Insert",
            Keys.Delete => "Delete",
            Keys.CapsLock => "Caps Lock",
            Keys.PrintScreen => "Print Screen",
            Keys.Pause => "Pause",
            Keys.Scroll => "Scroll Lock",
            Keys.OemSemicolon => ";",
            Keys.OemQuestion => "/",
            Keys.Oemtilde => "`",
            Keys.OemOpenBrackets => "[",
            Keys.OemPipe => "\\",
            Keys.OemCloseBrackets => "]",
            Keys.OemQuotes => "'",
            Keys.OemBackslash => "\\",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemMinus => "-",
            Keys.Oemplus => "+",
            Keys.Multiply => "*",
            Keys.Add => "+",
            Keys.Subtract => "-",
            Keys.Divide => "/",
            Keys.Decimal => ".",
            _ => keyCode.ToString()
        };
    }
}
