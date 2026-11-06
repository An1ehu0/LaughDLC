using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LaughDlc.Interop;
using LaughDlc.Models;
using LaughDlc.Services;

namespace LaughDlc.Controls;

public sealed class HotkeyCapturedEventArgs : EventArgs
{
    public HotkeyCapturedEventArgs(HotkeyDefinition hotkey)
    {
        Hotkey = hotkey;
    }

    public HotkeyDefinition Hotkey { get; }
}

public sealed class HotkeyCaptureValidationEventArgs : EventArgs
{
    public HotkeyCaptureValidationEventArgs(string message)
    {
        Message = message;
    }

    public string Message { get; }
}

public sealed class HotkeyRecorderControl : Button
{
    private HotkeyDefinition? _value;
    private bool _capturing;
    private bool _completionPending;
    private bool _invalidHintQueued;
    private int _invalidVirtualKey;
    private IntPtr _hook;
    private NativeMethods.LowLevelKeyboardProc? _hookProcedure;

    private bool _leftShift;
    private bool _rightShift;
    private bool _leftControl;
    private bool _rightControl;
    private bool _leftAlt;
    private bool _rightAlt;
    private bool _leftWin;
    private bool _rightWin;

    public HotkeyRecorderControl()
    {
        TextAlign = ContentAlignment.MiddleCenter;
        TabStop = true;
        AccessibleName = "快捷键录入";
        AccessibleDescription = "点击后按下包含 Ctrl、Alt、Shift 或 Win 的组合键。";
        UpdateText();
    }

    public event EventHandler<HotkeyCapturedEventArgs>? HotkeyCaptured;

    public event EventHandler<HotkeyCaptureValidationEventArgs>? ValidationFailed;

    public event EventHandler? CaptureCancelled;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public HotkeyDefinition? Value
    {
        get => _value;
        set
        {
            _value = value;
            if (!_capturing)
            {
                UpdateText();
            }
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "点击录入快捷键";

    public bool IsCapturing => _capturing;

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);

        if (_capturing)
        {
            CancelCapture();
        }
        else
        {
            BeginCapture();
        }
    }

    public void BeginCapture()
    {
        if (_capturing || IsDisposed)
        {
            return;
        }

        if (!Focused)
        {
            Focus();
        }

        _capturing = true;
        _completionPending = false;
        _invalidHintQueued = false;
        _invalidVirtualKey = 0;
        SeedModifierState();
        UpdateText();
        InstallHook();

        if (_hook == IntPtr.Zero)
        {
            _capturing = false;
            UpdateText();
        }
    }

    public void CancelCapture()
    {
        if (!_capturing)
        {
            return;
        }

        _capturing = false;
        _completionPending = false;
        _invalidHintQueued = false;
        _invalidVirtualKey = 0;
        UninstallHook();
        UpdateText();
        CaptureCancelled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        CancelCapture();
        base.OnLostFocus(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        if (!Visible)
        {
            CancelCapture();
        }

        base.OnVisibleChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled)
        {
            CancelCapture();
        }

        base.OnEnabledChanged(e);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        CancelCapture();
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelCapture();
        }

        base.Dispose(disposing);
    }

    private void InstallHook()
    {
        _hookProcedure = HookCallback;
        var moduleHandle = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _hookProcedure,
            moduleHandle,
            0);

        if (_hook != IntPtr.Zero)
        {
            return;
        }

        var win32Error = Marshal.GetLastWin32Error();
        _hookProcedure = null;
        AppLog.Error($"SetWindowsHookEx failed. Win32Error={win32Error}");
        var message =
            $"无法启用键盘录入监听（Win32 错误 {win32Error}）。请关闭并重新打开程序后重试。";
        PostToUi(() => ValidationFailed?.Invoke(this, new HotkeyCaptureValidationEventArgs(message)));
    }

    private void UninstallHook()
    {
        var hook = _hook;
        _hook = IntPtr.Zero;

        if (hook != IntPtr.Zero && !NativeMethods.UnhookWindowsHookEx(hook))
        {
            var win32Error = Marshal.GetLastWin32Error();
            AppLog.Error($"UnhookWindowsHookEx failed. Win32Error={win32Error}");
        }

        _hookProcedure = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_capturing)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        try
        {
            var message = wParam.ToInt32();
            var isKeyDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
            var isKeyUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;

            if (!isKeyDown && !isKeyUp)
            {
                return new IntPtr(1);
            }

            var info = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
            var virtualKey = unchecked((int)info.VkCode);

            if (virtualKey == NativeMethods.VkEscape && isKeyDown)
            {
                PostToUi(CancelCapture);
                return new IntPtr(1);
            }

            if (UpdateModifierState(virtualKey, isKeyDown))
            {
                return new IntPtr(1);
            }

            if (isKeyDown)
            {
                if (_completionPending)
                {
                    return new IntPtr(1);
                }

                var modifiers = GetModifiers();
                if (modifiers == HotkeyModifiers.None)
                {
                    QueueInvalidCombinationMessage(virtualKey);
                    return new IntPtr(1);
                }

                var key = (Keys)virtualKey;
                if (!IsUsableKey(key))
                {
                    QueueInvalidCombinationMessage(virtualKey);
                    return new IntPtr(1);
                }

                _completionPending = true;
                var definition = new HotkeyDefinition(modifiers, key);
                PostToUi(() => CompleteCapture(definition));
                return new IntPtr(1);
            }

            if (isKeyUp && virtualKey == _invalidVirtualKey)
            {
                _invalidHintQueued = false;
                _invalidVirtualKey = 0;
            }

            return new IntPtr(1);
        }
        catch (Exception ex)
        {
            AppLog.Error("Unhandled exception in low-level keyboard hook.", ex);
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    private void QueueInvalidCombinationMessage(int virtualKey)
    {
        if (_invalidHintQueued && _invalidVirtualKey == virtualKey)
        {
            return;
        }

        _invalidHintQueued = true;
        _invalidVirtualKey = virtualKey;
        const string message =
            "快捷键必须包含至少一个修饰键（Ctrl、Alt、Shift、Win）和一个普通按键。请继续录入，或按 Esc 取消。";
        PostToUi(() => ValidationFailed?.Invoke(this, new HotkeyCaptureValidationEventArgs(message)));
    }

    private bool UpdateModifierState(int virtualKey, bool isKeyDown)
    {
        switch (virtualKey)
        {
            case NativeMethods.VkLShift:
                _leftShift = isKeyDown;
                return true;
            case NativeMethods.VkRShift:
                _rightShift = isKeyDown;
                return true;
            case NativeMethods.VkLControl:
                _leftControl = isKeyDown;
                return true;
            case NativeMethods.VkRControl:
                _rightControl = isKeyDown;
                return true;
            case NativeMethods.VkLMenu:
                _leftAlt = isKeyDown;
                return true;
            case NativeMethods.VkRMenu:
                _rightAlt = isKeyDown;
                return true;
            case NativeMethods.VkLWin:
                _leftWin = isKeyDown;
                return true;
            case NativeMethods.VkRWin:
                _rightWin = isKeyDown;
                return true;
            default:
                return false;
        }
    }

    private HotkeyModifiers GetModifiers()
    {
        var modifiers = HotkeyModifiers.None;

        if (_leftControl || _rightControl)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (_leftAlt || _rightAlt)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (_leftShift || _rightShift)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (_leftWin || _rightWin)
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private void SeedModifierState()
    {
        _leftShift = IsKeyDown(NativeMethods.VkLShift);
        _rightShift = IsKeyDown(NativeMethods.VkRShift);
        _leftControl = IsKeyDown(NativeMethods.VkLControl);
        _rightControl = IsKeyDown(NativeMethods.VkRControl);
        _leftAlt = IsKeyDown(NativeMethods.VkLMenu);
        _rightAlt = IsKeyDown(NativeMethods.VkRMenu);
        _leftWin = IsKeyDown(NativeMethods.VkLWin);
        _rightWin = IsKeyDown(NativeMethods.VkRWin);
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static bool IsUsableKey(Keys key)
    {
        return key != Keys.None
            && !HotkeyDefinition.IsModifierKey(key)
            && (key & Keys.Modifiers) == Keys.None;
    }

    private void CompleteCapture(HotkeyDefinition definition)
    {
        if (!_capturing)
        {
            return;
        }

        _capturing = false;
        _completionPending = false;
        _invalidHintQueued = false;
        _invalidVirtualKey = 0;
        UninstallHook();
        UpdateText();

        HotkeyCaptured?.Invoke(this, new HotkeyCapturedEventArgs(definition));
    }

    private void UpdateText()
    {
        Text = _capturing
            ? "请按下组合键…（Esc 取消）"
            : _value?.DisplayText ?? EmptyText;
    }

    private void PostToUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            if (IsHandleCreated)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (InvalidOperationException)
        {
            // The control is being torn down.
        }
    }
}
