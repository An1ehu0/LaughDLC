using NAudio.Wave;

namespace LaughDlc.Services;

public enum AudioPlayerState
{
    Idle,
    Playing,
    Completed,
    Failed
}

public sealed class AudioPlayerStateChangedEventArgs : EventArgs
{
    public AudioPlayerStateChangedEventArgs(AudioPlayerState state, string? filePath, string? errorMessage)
    {
        State = state;
        FilePath = filePath;
        ErrorMessage = errorMessage;
    }

    public AudioPlayerState State { get; }

    public string? FilePath { get; }

    public string? ErrorMessage { get; }
}

public sealed class AudioPlaybackException : Exception
{
    public AudioPlaybackException(string message, string filePath, Exception? innerException = null)
        : base(message, innerException)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
}

public sealed class AudioPlayer : IDisposable
{
    private readonly object _gate = new();
    private readonly SynchronizationContext? _synchronizationContext;
    private IWavePlayer? _output;
    private WaveStream? _reader;
    private EventHandler<StoppedEventArgs>? _stoppedHandler;
    private string? _currentPath;
    private int _generation;
    private bool _disposed;

    public AudioPlayer()
    {
        _synchronizationContext = SynchronizationContext.Current;
    }

    public event EventHandler<AudioPlayerStateChangedEventArgs>? StateChanged;

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return !_disposed
                    && _output is not null
                    && _output.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    public string? CurrentFilePath
    {
        get
        {
            lock (_gate)
            {
                return _currentPath;
            }
        }
    }

    public void Play(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new AudioPlaybackException("请先选择音频文件。", string.Empty);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AudioPlaybackException(
                $"音频路径无效：{filePath}\n\n请重新选择音频文件。\n\n详情：{ex.Message}",
                filePath,
                ex);
        }

        var extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            throw new AudioPlaybackException(
                $"不支持的音频格式：{Path.GetFileName(fullPath)}\n\n当前仅支持 WAV 和 MP3。请选择 .wav 或 .mp3 文件。",
                fullPath);
        }

        if (!File.Exists(fullPath))
        {
            throw new AudioPlaybackException(
                $"无法播放“{Path.GetFileName(fullPath)}”：文件不存在或已被移动。\n\n请重新选择音频文件。\n\n路径：{fullPath}",
                fullPath);
        }

        StopCore();

        WaveStream? reader = null;
        try
        {
            reader = CreateReader(fullPath);
        }
        catch (Exception ex)
        {
            reader?.Dispose();
            AppLog.Error($"Failed to read audio file. Path={fullPath}", ex);
            throw new AudioPlaybackException(
                $"无法读取“{Path.GetFileName(fullPath)}”。\n\n" +
                "文件可能已损坏、被占用，或不是有效的 WAV/MP3 文件。" +
                "请先用其他播放器确认文件可播放，然后重新选择。\n\n" +
                $"详情：{ex.Message}",
                fullPath,
                ex);
        }

        IWavePlayer? output = null;
        EventHandler<StoppedEventArgs>? stoppedHandler = null;

        try
        {
            output = new WasapiPlayerBuilder()
                .WithSharedMode()
                .WithLatency(100)
                .Build();
            output.Init(reader);
            stoppedHandler = OnPlaybackStopped;

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _generation++;
                _output = output;
                _reader = reader;
                _stoppedHandler = stoppedHandler;
                _currentPath = fullPath;
                output.PlaybackStopped += stoppedHandler;
            }

            output.Play();
        }
        catch (Exception ex)
        {
            DetachFailedPlayback(output, reader, stoppedHandler);
            AppLog.Error($"Failed to start audio playback. Path={fullPath}", ex);
            throw new AudioPlaybackException(
                $"无法启动音频输出。\n\n请检查 Windows 默认播放设备、音量和服务后重试。\n\n" +
                $"文件：{Path.GetFileName(fullPath)}\n详情：{ex.Message}",
                fullPath,
                ex);
        }

        RaiseStateChanged(AudioPlayerState.Playing, fullPath, null);
    }

    public void Stop()
    {
        string? previousPath;
        bool hasResources;

        lock (_gate)
        {
            previousPath = _currentPath;
            hasResources = _output is not null || _reader is not null;
        }

        if (_disposed)
        {
            return;
        }

        StopCore();

        if (hasResources)
        {
            RaiseStateChanged(AudioPlayerState.Idle, previousPath, null);
        }
    }

    private void StopCore()
    {
        IWavePlayer? output;
        WaveStream? reader;
        EventHandler<StoppedEventArgs>? stoppedHandler;
        string? previousPath;

        lock (_gate)
        {
            output = _output;
            reader = _reader;
            stoppedHandler = _stoppedHandler;
            previousPath = _currentPath;

            _output = null;
            _reader = null;
            _stoppedHandler = null;
            _currentPath = null;
            _generation++;
        }

        if (output is not null && stoppedHandler is not null)
        {
            try
            {
                output.PlaybackStopped -= stoppedHandler;
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to detach PlaybackStopped handler.", ex);
            }
        }

        try
        {
            output?.Stop();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to stop audio output. Path={previousPath}", ex);
        }

        try
        {
            output?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to dispose audio output. Path={previousPath}", ex);
        }

        try
        {
            reader?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to dispose audio reader. Path={previousPath}", ex);
        }
    }

    private void DetachFailedPlayback(
        IWavePlayer? output,
        WaveStream? reader,
        EventHandler<StoppedEventArgs>? stoppedHandler)
    {
        if (output is not null && stoppedHandler is not null)
        {
            try
            {
                output.PlaybackStopped -= stoppedHandler;
            }
            catch
            {
                // Best effort during failure cleanup.
            }
        }

        lock (_gate)
        {
            if (ReferenceEquals(_output, output))
            {
                _output = null;
                _reader = null;
                _stoppedHandler = null;
                _currentPath = null;
            }

            _generation++;
        }

        try
        {
            output?.Stop();
        }
        catch
        {
            // Best effort during failure cleanup.
        }

        try
        {
            output?.Dispose();
        }
        catch
        {
            // Best effort during failure cleanup.
        }

        try
        {
            reader?.Dispose();
        }
        catch
        {
            // Best effort during failure cleanup.
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        int generation;
        string? path;

        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(sender, _output))
            {
                return;
            }

            generation = _generation;
            path = _currentPath;

            if (_output is not null && _stoppedHandler is not null)
            {
                try
                {
                    _output.PlaybackStopped -= _stoppedHandler;
                }
                catch
                {
                    // Best effort while handling playback completion.
                }
            }
        }

        var completion = new PlaybackCompletion(generation, path, args.Exception);
        if (_synchronizationContext is not null)
        {
            _synchronizationContext.Post(FinishPlayback, completion);
        }
        else
        {
            FinishPlayback(completion);
        }
    }

    private void FinishPlayback(object? state)
    {
        if (state is not PlaybackCompletion completion)
        {
            return;
        }

        IWavePlayer? output;
        WaveStream? reader;
        string? path;

        lock (_gate)
        {
            if (_disposed || completion.Generation != _generation || _output is null)
            {
                return;
            }

            output = _output;
            reader = _reader;
            path = _currentPath;

            _output = null;
            _reader = null;
            _stoppedHandler = null;
            _currentPath = null;
            _generation++;
        }

        try
        {
            output?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to dispose completed audio output. Path={path}", ex);
        }

        try
        {
            reader?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to dispose completed audio reader. Path={path}", ex);
        }

        if (completion.Exception is not null)
        {
            AppLog.Error($"Audio playback failed asynchronously. Path={path}", completion.Exception);
            var message =
                $"播放“{Path.GetFileName(path ?? string.Empty)}”时发生错误。\n\n" +
                "请检查音频文件是否仍可访问，以及 Windows 默认播放设备是否正常。\n\n" +
                $"详情：{completion.Exception.Message}";
            RaiseStateChanged(AudioPlayerState.Failed, path, message);
        }
        else
        {
            RaiseStateChanged(AudioPlayerState.Completed, path, null);
        }
    }

    private void RaiseStateChanged(AudioPlayerState state, string? path, string? errorMessage)
    {
        try
        {
            StateChanged?.Invoke(this, new AudioPlayerStateChangedEventArgs(state, path, errorMessage));
        }
        catch (Exception ex)
        {
            AppLog.Error("Unhandled exception in audio state event handler.", ex);
        }
    }

    private static WaveStream CreateReader(string fullPath)
    {
        var extension = Path.GetExtension(fullPath);
        return extension.ToLowerInvariant() switch
        {
            ".wav" => new WaveFileReader(fullPath),
            ".mp3" => new MediaFoundationReader(fullPath),
            _ => throw new NotSupportedException("Only WAV and MP3 files are supported.")
        };
    }

    public void Dispose()
    {
        IWavePlayer? output;
        WaveStream? reader;
        EventHandler<StoppedEventArgs>? stoppedHandler;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            output = _output;
            reader = _reader;
            stoppedHandler = _stoppedHandler;
            _output = null;
            _reader = null;
            _stoppedHandler = null;
            _currentPath = null;
            _generation++;
        }

        if (output is not null && stoppedHandler is not null)
        {
            try
            {
                output.PlaybackStopped -= stoppedHandler;
            }
            catch
            {
                // Best effort during dispose.
            }
        }

        try
        {
            output?.Stop();
        }
        catch
        {
            // Best effort during dispose.
        }

        try
        {
            output?.Dispose();
        }
        catch
        {
            // Best effort during dispose.
        }

        try
        {
            reader?.Dispose();
        }
        catch
        {
            // Best effort during dispose.
        }
    }

    private sealed record PlaybackCompletion(int Generation, string? Path, Exception? Exception);
}
