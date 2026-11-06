using LaughDlc.Controls;
using LaughDlc.Interop;
using LaughDlc.Models;
using LaughDlc.Services;

namespace LaughDlc;

public partial class MainForm : Form
{
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly HotkeyService _hotkeyService;
    private readonly AudioPlayer _audioPlayer;

    private string? _startupWarning;
    private HotkeyRegistration? _activeHotkey;
    private string? _lastHotkeyError;
    private bool _isExiting;
    private bool _resourcesDisposed;
    private bool _updatingUi;
    private bool _trayHintShown;
    private bool _applyingHotkeyChange;

    public MainForm(SettingsStore settingsStore, SettingsLoadResult loadResult)
    {
        _settingsStore = settingsStore;
        _settings = loadResult.Settings;
        _startupWarning = loadResult.WarningMessage;

        InitializeComponent();

        Icon = SystemIcons.Application;
        trayIcon.Icon = SystemIcons.Application;

        _hotkeyService = new HotkeyService();
        _audioPlayer = new AudioPlayer();

        WireEvents();

        hotkeyRecorder.Value = _settings.Hotkey.Clone();
        hotkeyEnabledCheckBox.Checked = _settings.HotkeyEnabled;

        if (_settings.HotkeyEnabled)
        {
            RegisterCurrentHotkeyAtStartup();
        }

        UpdateAllUi();
        AppLog.Info("Main window initialized.");
    }

    private void WireEvents()
    {
        browseButton.Click += (_, _) => ChooseAudioFile();
        playButton.Click += (_, _) => PlayCurrentAudio(fromTray: false);
        stopButton.Click += (_, _) => StopPlayback();

        hotkeyEnabledCheckBox.CheckedChanged += OnHotkeyEnabledChanged;
        hotkeyRecorder.HotkeyCaptured += OnHotkeyCaptured;
        hotkeyRecorder.ValidationFailed += OnHotkeyCaptureValidationFailed;
        hotkeyRecorder.CaptureCancelled += (_, _) =>
        {
            if (!IsDisposed && !_resourcesDisposed)
            {
                UpdateHotkeyUi();
            }
        };

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _audioPlayer.StateChanged += OnAudioStateChanged;

        Shown += OnShown;
        Deactivate += (_, _) => hotkeyRecorder.CancelCapture();
        FormClosing += OnFormClosing;
        FormClosed += (_, _) => CleanupForExit();

        trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        trayMenu.Opening += (_, _) => UpdateTrayState();

        openMenuItem.Click += (_, _) => ShowMainWindow();
        playMenuItem.Click += (_, _) => PlayCurrentAudio(fromTray: true);
        stopMenuItem.Click += (_, _) => StopPlayback();
        toggleHotkeyMenuItem.Click += (_, _) => ChangeHotkeyEnabled(!_settings.HotkeyEnabled);
        exitMenuItem.Click += (_, _) => ExitApplication();

        openMenuItem.Font = new Font(openMenuItem.Font, FontStyle.Bold);
    }

    private void OnShown(object? sender, EventArgs e)
    {
        if (_startupWarning is not null)
        {
            var warning = _startupWarning;
            _startupWarning = null;
            MessageBox.Show(
                this,
                warning,
                "配置恢复",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        if (!string.IsNullOrWhiteSpace(_settings.AudioFilePath)
            && !File.Exists(_settings.AudioFilePath))
        {
            SetPlaybackStatus(
                $"音频文件不存在：{_settings.AudioFilePath}\n请点击“选择音频…”重新选择。",
                isError: true);
        }

        if (_settings.HotkeyEnabled && _activeHotkey is null && _lastHotkeyError is not null)
        {
            trayIcon.ShowBalloonTip(
                5000,
                "全局快捷键注册失败",
                _lastHotkeyError,
                ToolTipIcon.Warning);
        }
    }

    private void RegisterCurrentHotkeyAtStartup()
    {
        _activeHotkey = _hotkeyService.TryRegister(_settings.Hotkey, out var error);
        if (_activeHotkey is not null)
        {
            _lastHotkeyError = null;
            return;
        }

        _lastHotkeyError = error?.Message ?? "未知的注册错误。";
        AppLog.Error($"Failed to register saved hotkey at startup. Hotkey={_settings.Hotkey.DisplayText}; {_lastHotkeyError}");
    }

    private void ChooseAudioFile()
    {
        hotkeyRecorder.CancelCapture();

        using var dialog = new OpenFileDialog
        {
            Title = "选择音频文件",
            Filter = "支持的音频 (*.wav;*.mp3)|*.wav;*.mp3|WAV 音频 (*.wav)|*.wav|MP3 音频 (*.mp3)|*.mp3",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var newPath = dialog.FileName;
        if (string.Equals(newPath, _settings.AudioFilePath, StringComparison.OrdinalIgnoreCase))
        {
            UpdateAudioUi();
            return;
        }

        _audioPlayer.Stop();
        _settings.AudioFilePath = newPath;

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to save settings after selecting an audio file.", ex);
            MessageBox.Show(
                this,
                $"{ex.Message}\n\n音频路径已在本窗口中更新，本次运行仍可播放，但重启后可能不会保留。",
                "无法保存设置",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        UpdateAllUi();
        SetPlaybackStatus(
            $"已选择：{Path.GetFileName(newPath)}\n点击“试听”开始播放。",
            isError: false);
    }

    private void PlayCurrentAudio(bool fromTray)
    {
        if (_isExiting)
        {
            return;
        }

        var path = _settings.AudioFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            if (fromTray)
            {
                ShowMainWindow();
            }

            MessageBox.Show(
                this,
                "请先选择音频文件。\n\n当前仅支持 WAV 和 MP3。",
                "尚未选择音频",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            _audioPlayer.Play(path);
            SetPlaybackStatus($"正在播放：{Path.GetFileName(path)}", isError: false);
        }
        catch (AudioPlaybackException ex)
        {
            HandlePlaybackFailure(ex.Message, fromTray);
        }
        catch (Exception ex)
        {
            AppLog.Error("Unexpected error while starting playback.", ex);
            HandlePlaybackFailure(
                $"播放时发生未预期错误。\n\n详情：{ex.Message}",
                fromTray);
        }

        UpdatePlaybackControls();
        UpdateTrayState();
    }

    private void StopPlayback()
    {
        _audioPlayer.Stop();
        SetPlaybackStatus("已停止。", isError: false);
        UpdatePlaybackControls();
        UpdateTrayState();
    }

    private void OnAudioStateChanged(object? sender, AudioPlayerStateChangedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => OnAudioStateChanged(sender, e)));
            }
            catch (InvalidOperationException)
            {
                // Form is being torn down.
            }

            return;
        }

        var fileName = string.IsNullOrWhiteSpace(e.FilePath)
            ? "音频"
            : Path.GetFileName(e.FilePath);

        switch (e.State)
        {
            case AudioPlayerState.Playing:
                SetPlaybackStatus($"正在播放：{fileName}", isError: false);
                break;
            case AudioPlayerState.Completed:
                SetPlaybackStatus($"播放完毕：{fileName}", isError: false);
                break;
            case AudioPlayerState.Idle:
                SetPlaybackStatus("已停止。", isError: false);
                break;
            case AudioPlayerState.Failed:
                SetPlaybackStatus($"播放失败：{fileName}", isError: true);
                HandlePlaybackFailure(e.ErrorMessage ?? "播放失败，原因未知。", fromTray: !Visible);
                break;
        }

        UpdatePlaybackControls();
        UpdateTrayState();
    }

    private void HandlePlaybackFailure(string message, bool fromTray)
    {
        SetPlaybackStatus(message, isError: true);

        if (fromTray && !Visible)
        {
            trayIcon.ShowBalloonTip(
                7000,
                "播放失败",
                message,
                ToolTipIcon.Error);
        }

        if (!Visible)
        {
            ShowMainWindow();
        }

        MessageBox.Show(
            this,
            message,
            "播放错误",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        if (_isExiting || _applyingHotkeyChange || hotkeyRecorder.IsCapturing)
        {
            return;
        }

        PlayCurrentAudio(fromTray: !Visible);
    }

    private void OnHotkeyCaptured(object? sender, HotkeyCapturedEventArgs e)
    {
        ApplyHotkeyChange(e.Hotkey);
    }

    private void OnHotkeyCaptureValidationFailed(object? sender, HotkeyCaptureValidationEventArgs e)
    {
        hotkeyStatusLabel.ForeColor = Color.Firebrick;
        hotkeyStatusLabel.Text = e.Message;
    }

    private void ApplyHotkeyChange(HotkeyDefinition newHotkey)
    {
        if (_applyingHotkeyChange)
        {
            return;
        }

        if (!newHotkey.IsValid)
        {
            ReportHotkeyProblem("新的快捷键无效：必须包含至少一个修饰键和一个普通按键。旧快捷键保持不变。");
            UpdateHotkeyUi();
            return;
        }

        if (_settings.Hotkey.Equals(newHotkey)
            && (!_settings.HotkeyEnabled || _activeHotkey is not null))
        {
            _lastHotkeyError = null;
            UpdateAllUi();
            return;
        }

        _applyingHotkeyChange = true;
        try
        {
            var previousSettings = _settings.Clone();
            var previousRegistration = _activeHotkey;
            HotkeyRegistration? candidate = null;
            HotkeyRegistrationError? registrationError = null;

            if (_settings.HotkeyEnabled)
            {
                candidate = _hotkeyService.TryRegister(newHotkey, out registrationError);
                if (candidate is null)
                {
                    _lastHotkeyError = registrationError?.Message ?? "注册新快捷键失败。";
                    UpdateHotkeyUi();
                    ReportHotkeyProblem(
                        $"无法使用新快捷键“{newHotkey.DisplayText}”。\n\n{_lastHotkeyError}\n\n当前配置和快捷键保持不变。");
                    return;
                }

                previousRegistration?.Dispose();
                _activeHotkey = candidate;
            }

            var candidateSettings = previousSettings.Clone();
            candidateSettings.Hotkey = newHotkey.Clone();

            try
            {
                _settingsStore.Save(candidateSettings);
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to save settings for hotkey change; rolling back.", ex);

                candidate?.Dispose();
                _activeHotkey = null;

                var rollbackSucceeded = true;
                HotkeyRegistrationError? rollbackError = null;

                if (previousSettings.HotkeyEnabled && previousRegistration is not null)
                {
                    var rollback = _hotkeyService.TryRegister(previousSettings.Hotkey, out rollbackError);
                    _activeHotkey = rollback;
                    rollbackSucceeded = rollback is not null;
                }

                if (rollbackSucceeded)
                {
                    _lastHotkeyError = previousSettings.HotkeyEnabled && _activeHotkey is null
                        ? _lastHotkeyError
                        : null;
                    UpdateAllUi();
                    ReportHotkeyProblem(
                        "无法保存配置，快捷键修改未生效。\n\n" +
                        $"旧快捷键“{previousSettings.Hotkey.DisplayText}”已恢复。\n\n{ex.Message}");
                }
                else
                {
                    _lastHotkeyError =
                        $"无法保存配置，且旧快捷键恢复注册失败：{rollbackError?.Message ?? "未知错误"}";
                    UpdateAllUi();
                    ReportHotkeyProblem(
                        $"无法保存配置，且无法恢复旧快捷键。\n\n{_lastHotkeyError}\n\n" +
                        "请检查系统快捷键冲突，并重新设置快捷键。");
                }

                return;
            }

            _settings.Hotkey = newHotkey.Clone();
            _settings.HotkeyEnabled = previousSettings.HotkeyEnabled;
            _lastHotkeyError = null;
            UpdateAllUi();
            AppLog.Info($"Hotkey configuration committed. Hotkey={_settings.Hotkey.DisplayText}; Enabled={_settings.HotkeyEnabled}");
        }
        finally
        {
            _applyingHotkeyChange = false;
        }
    }

    private void OnHotkeyEnabledChanged(object? sender, EventArgs e)
    {
        if (_updatingUi)
        {
            return;
        }

        ChangeHotkeyEnabled(hotkeyEnabledCheckBox.Checked);
    }

    private void ChangeHotkeyEnabled(bool enabled)
    {
        if (_applyingHotkeyChange || enabled == _settings.HotkeyEnabled)
        {
            UpdateHotkeyUi();
            return;
        }

        _applyingHotkeyChange = true;
        try
        {
            if (enabled)
            {
                var registration = _hotkeyService.TryRegister(_settings.Hotkey, out var error);
                if (registration is null)
                {
                    _lastHotkeyError = error?.Message ?? "注册快捷键失败。";
                    UpdateHotkeyUi();
                    ReportHotkeyProblem(
                        $"无法启用快捷键“{_settings.Hotkey.DisplayText}”。\n\n{_lastHotkeyError}");
                    return;
                }

                var candidate = _settings.Clone();
                candidate.HotkeyEnabled = true;

                try
                {
                    _settingsStore.Save(candidate);
                }
                catch (Exception ex)
                {
                    registration.Dispose();
                    _lastHotkeyError = $"启用失败，配置无法保存：{ex.Message}";
                    UpdateHotkeyUi();
                    ReportHotkeyProblem(_lastHotkeyError);
                    return;
                }

                _settings.HotkeyEnabled = true;
                _activeHotkey = registration;
                _lastHotkeyError = null;
                UpdateAllUi();
                AppLog.Info($"Hotkey enabled. Hotkey={_settings.Hotkey.DisplayText}");
                return;
            }

            var activeRegistration = _activeHotkey;
            activeRegistration?.Dispose();
            _activeHotkey = null;

            var disabledSettings = _settings.Clone();
            disabledSettings.HotkeyEnabled = false;

            try
            {
                _settingsStore.Save(disabledSettings);
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to save disabled hotkey state; rolling back registration.", ex);

                HotkeyRegistration? rollback = null;
                HotkeyRegistrationError? rollbackError = null;
                if (activeRegistration is not null)
                {
                    rollback = _hotkeyService.TryRegister(_settings.Hotkey, out rollbackError);
                }

                _activeHotkey = rollback;
                _lastHotkeyError = rollback is null
                    ? $"无法保存配置，也无法恢复快捷键注册：{rollbackError?.Message ?? "未知错误"}"
                    : $"无法保存配置，快捷键仍保持启用。\n\n{ex.Message}";
                UpdateAllUi();
                ReportHotkeyProblem(_lastHotkeyError);
                return;
            }

            _settings.HotkeyEnabled = false;
            _lastHotkeyError = null;
            UpdateAllUi();
            AppLog.Info("Hotkey disabled.");
        }
        finally
        {
            _applyingHotkeyChange = false;
        }
    }

    private void ReportHotkeyProblem(string message)
    {
        AppLog.Error(message);

        if (Visible)
        {
            MessageBox.Show(
                this,
                message,
                "快捷键",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        trayIcon.ShowBalloonTip(
            7000,
            "快捷键不可用",
            message,
            ToolTipIcon.Warning);
    }

    private void UpdateAllUi()
    {
        UpdateAudioUi();
        UpdateHotkeyUi();
        UpdatePlaybackControls();
        UpdateTrayState();
    }

    private void UpdateAudioUi()
    {
        var path = _settings.AudioFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            audioPathTextBox.Text = "（尚未选择音频文件）";
            audioPathTextBox.ForeColor = SystemColors.GrayText;
            playButton.Enabled = false;
            return;
        }

        audioPathTextBox.Text = path;
        audioPathTextBox.ForeColor = File.Exists(path) ? SystemColors.WindowText : Color.Firebrick;
        playButton.Enabled = true;
    }

    private void UpdateHotkeyUi()
    {
        if (!_updatingUi)
        {
            _updatingUi = true;
            try
            {
                hotkeyEnabledCheckBox.Checked = _settings.HotkeyEnabled;
            }
            finally
            {
                _updatingUi = false;
            }
        }

        hotkeyRecorder.Value = _settings.Hotkey.Clone();

        if (_settings.HotkeyEnabled)
        {
            if (_activeHotkey is not null)
            {
                if (_lastHotkeyError is not null)
                {
                    hotkeyStatusLabel.ForeColor = Color.Firebrick;
                    hotkeyStatusLabel.Text = $"状态：已启用（全局）\r\n{_lastHotkeyError}";
                }
                else
                {
                    hotkeyStatusLabel.ForeColor = Color.ForestGreen;
                    hotkeyStatusLabel.Text = "状态：已启用（全局）";
                }
            }
            else
            {
                hotkeyStatusLabel.ForeColor = Color.Firebrick;
                hotkeyStatusLabel.Text =
                    $"状态：注册失败\r\n{_lastHotkeyError ?? "未知原因，请重新设置快捷键。"}";
            }
        }
        else if (_lastHotkeyError is not null)
        {
            hotkeyStatusLabel.ForeColor = Color.Firebrick;
            hotkeyStatusLabel.Text = $"状态：已禁用\r\n{_lastHotkeyError}";
        }
        else
        {
            hotkeyStatusLabel.ForeColor = SystemColors.GrayText;
            hotkeyStatusLabel.Text = "状态：已禁用";
        }
    }

    private void UpdatePlaybackControls()
    {
        var hasPath = !string.IsNullOrWhiteSpace(_settings.AudioFilePath);
        var isPlaying = _audioPlayer.IsPlaying;

        playButton.Enabled = hasPath && !_isExiting;
        stopButton.Enabled = isPlaying && !_isExiting;
        browseButton.Enabled = !_isExiting;
        hotkeyRecorder.Enabled = !_isExiting;
        hotkeyEnabledCheckBox.Enabled = !_isExiting;
    }

    private void UpdateTrayState()
    {
        var hasPath = !string.IsNullOrWhiteSpace(_settings.AudioFilePath);
        var isPlaying = _audioPlayer.IsPlaying;

        openMenuItem.Enabled = !Visible;
        playMenuItem.Enabled = hasPath;
        stopMenuItem.Enabled = isPlaying;
        toggleHotkeyMenuItem.Checked = _settings.HotkeyEnabled;
        toggleHotkeyMenuItem.Text = _settings.HotkeyEnabled
            ? "禁用快捷键(&H)"
            : "启用快捷键(&H)";
        playButton.Enabled = hasPath && !_isExiting;
        stopButton.Enabled = isPlaying && !_isExiting;

        var hotkeyState = !_settings.HotkeyEnabled
            ? "已禁用"
            : _activeHotkey is not null
                ? "已启用"
                : "注册失败";
        var trayText = $"笑声DLC - {_settings.Hotkey.DisplayText} ({hotkeyState})";
        trayIcon.Text = trayText.Length <= 63 ? trayText : trayText[..63];
    }

    private void SetPlaybackStatus(string text, bool isError)
    {
        playbackStatusLabel.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;
        playbackStatusLabel.Text = text;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        hotkeyRecorder.CancelCapture();
        AppLog.Info($"Form closing. Reason={e.CloseReason}; ExplicitExit={_isExiting}");

        if (_isExiting
            || e.CloseReason is CloseReason.WindowsShutDown or CloseReason.ApplicationExitCall)
        {
            CleanupForExit();
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        hotkeyRecorder.CancelCapture();
        ShowInTaskbar = false;
        Hide();
        UpdateTrayState();

        if (!_trayHintShown)
        {
            _trayHintShown = true;
            trayIcon.ShowBalloonTip(
                4000,
                "笑声DLC",
                "程序已隐藏到系统托盘。双击托盘图标可重新打开，右键菜单可退出。",
                ToolTipIcon.Info);
        }
    }

    public void RequestActivation()
    {
        if (IsDisposed || _isExiting)
        {
            return;
        }

        try
        {
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(ShowMainWindow));
            }
            else
            {
                AppLog.Error("Activation request received before the main window handle was created.");
            }
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Failed to queue activation request.", ex);
        }
    }

    private void ShowMainWindow()
    {
        if (IsDisposed || _isExiting)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(ShowMainWindow));
            }
            catch (InvalidOperationException)
            {
                // Form is being torn down.
            }

            return;
        }

        ShowInTaskbar = true;

        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        BringToFront();
        ForceForeground();
        UpdateTrayState();
    }

    private void ForceForeground()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            }

            NativeMethods.ShowWindow(Handle, NativeMethods.SwRestore);
            NativeMethods.SetForegroundWindow(Handle);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        hotkeyRecorder.CancelCapture();
        CleanupForExit();
        Close();
    }

    private void CleanupForExit()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        _resourcesDisposed = true;
        AppLog.Info("Starting exit cleanup.");

        try
        {
            hotkeyRecorder.CancelCapture();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to cancel hotkey capture during exit.", ex);
        }

        try
        {
            _activeHotkey?.Dispose();
            _activeHotkey = null;
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to unregister hotkey during exit.", ex);
        }

        try
        {
            _hotkeyService.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to dispose hotkey service during exit.", ex);
        }

        try
        {
            _audioPlayer.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to dispose audio player during exit.", ex);
        }

        try
        {
            trayIcon.Visible = false;
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to hide tray icon during exit.", ex);
        }

        AppLog.Info("Exit cleanup completed.");
    }
}
