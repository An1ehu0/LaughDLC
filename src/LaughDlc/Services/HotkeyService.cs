using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LaughDlc.Interop;
using LaughDlc.Models;

namespace LaughDlc.Services;

public sealed record HotkeyRegistrationError(int Win32Error, string Message);

public sealed class HotkeyPressedEventArgs : EventArgs
{
    public HotkeyPressedEventArgs(HotkeyDefinition definition)
    {
        Definition = definition;
    }

    public HotkeyDefinition Definition { get; }
}

public sealed class HotkeyRegistration : IDisposable
{
    private readonly HotkeyService _owner;
    private readonly int _id;
    private bool _registered = true;

    internal HotkeyRegistration(HotkeyService owner, int id, HotkeyDefinition definition)
    {
        _owner = owner;
        _id = id;
        Definition = definition;
    }

    public HotkeyDefinition Definition { get; }

    public bool IsRegistered => _registered;

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        _registered = false;
        _owner.Unregister(_id);
    }
}

public sealed class HotkeyService : IDisposable
{
    private const int FirstHotkeyId = 0xB000;
    private const int LastHotkeyId = 0xBFFF;

    private readonly object _gate = new();
    private readonly HotkeyMessageWindow _window;
    private readonly Dictionary<int, HotkeyDefinition> _registrations = new();
    private int _nextId = FirstHotkeyId;
    private bool _disposed;

    public HotkeyService()
    {
        _window = new HotkeyMessageWindow(this);
    }

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public HotkeyRegistration? TryRegister(HotkeyDefinition definition, out HotkeyRegistrationError? error)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!definition.IsValid)
        {
            error = new HotkeyRegistrationError(0, "快捷键必须包含至少一个修饰键（Ctrl、Alt、Shift、Win）和一个普通按键。");
            return null;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                error = new HotkeyRegistrationError(0, "快捷键服务已关闭。");
                return null;
            }

            var id = AllocateId();
            var modifiers = (uint)definition.Modifiers | NativeMethods.ModNoRepeat;
            var virtualKey = (uint)(definition.Key & Keys.KeyCode);

            if (!NativeMethods.RegisterHotKey(_window.Handle, id, modifiers, virtualKey))
            {
                var win32Error = Marshal.GetLastWin32Error();
                error = new HotkeyRegistrationError(win32Error, DescribeRegistrationFailure(win32Error));
                AppLog.Error(
                    $"RegisterHotKey failed. Hotkey={definition.DisplayText}; Win32Error={win32Error}; " +
                    $"Message={error.Message}");
                return null;
            }

            _registrations[id] = definition.Clone();
            error = null;
            AppLog.Info($"Hotkey registered. Hotkey={definition.DisplayText}; Id=0x{id:X}");
            return new HotkeyRegistration(this, id, definition.Clone());
        }
    }

    internal void Unregister(int id)
    {
        lock (_gate)
        {
            if (!_registrations.Remove(id))
            {
                return;
            }

            if (!_disposed && !NativeMethods.UnregisterHotKey(_window.Handle, id))
            {
                var win32Error = Marshal.GetLastWin32Error();
                AppLog.Error($"UnregisterHotKey failed. Id=0x{id:X}; Win32Error={win32Error}");
            }
        }
    }

    private int AllocateId()
    {
        var range = LastHotkeyId - FirstHotkeyId + 1;
        for (var attempt = 0; attempt < range; attempt++)
        {
            var candidate = _nextId;
            _nextId++;
            if (_nextId > LastHotkeyId)
            {
                _nextId = FirstHotkeyId;
            }

            if (!_registrations.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("没有可用的全局快捷键标识符。");
    }

    private void HandleHotkey(int id)
    {
        HotkeyDefinition? definition;
        lock (_gate)
        {
            _registrations.TryGetValue(id, out definition);
        }

        if (definition is null)
        {
            return;
        }

        try
        {
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(definition));
        }
        catch (Exception ex)
        {
            AppLog.Error("Unhandled exception in hotkey event handler.", ex);
        }
    }

    private static string DescribeRegistrationFailure(int win32Error)
    {
        return win32Error switch
        {
            5 => "该组合键被系统保留，或当前权限不足，无法注册。请换一个组合键。",
            87 => "该快捷键参数无效，请换一个组合键。",
            1409 => "该组合键已被系统或其他程序占用，请换一个组合键。",
            _ => $"注册全局快捷键失败（Win32 错误 {win32Error}）：{new Win32Exception(win32Error).Message}"
        };
    }

    public void Dispose()
    {
        HotkeyMessageWindow window;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var id in _registrations.Keys.ToArray())
            {
                if (!NativeMethods.UnregisterHotKey(_window.Handle, id))
                {
                    var win32Error = Marshal.GetLastWin32Error();
                    AppLog.Error($"UnregisterHotKey during dispose failed. Id=0x{id:X}; Win32Error={win32Error}");
                }
            }

            _registrations.Clear();
            window = _window;
        }

        window.Dispose();
    }

    private sealed class HotkeyMessageWindow : NativeWindow, IDisposable
    {
        private readonly HotkeyService _owner;
        private bool _disposed;

        public HotkeyMessageWindow(HotkeyService owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams
            {
                Caption = "LaughDlc.HotkeyWindow",
                Parent = NativeMethods.HwndMessage
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WmHotkey)
            {
                _owner.HandleHotkey((int)m.WParam);
                return;
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DestroyHandle();
        }
    }
}
