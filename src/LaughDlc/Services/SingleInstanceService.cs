using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace LaughDlc.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string ActivationMessage = "ACTIVATE";
    private static readonly TimeSpan ServerReadTimeout = TimeSpan.FromSeconds(3);

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private readonly string _pipeName;
    private readonly object _gate = new();

    private CancellationTokenSource? _cancellation;
    private Task? _serverTask;
    private NamedPipeServerStream? _activeServer;
    private Action? _activationHandler;
    private bool _disposed;

    private SingleInstanceService(Mutex mutex, bool ownsMutex, string pipeName)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
        _pipeName = pipeName;
    }

    public bool IsPrimary { get; private init; }

    public static SingleInstanceService Acquire()
    {
        var userKey = GetUserAndSessionKey();
        var mutexName = $@"Local\LaughDlc.SingleInstance.{userKey}";
        var pipeName = $"LaughDlc.Activate.{userKey}";

        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);

        if (!createdNew)
        {
            try
            {
                // If the previous owner crashed, the mutex is abandoned and we can take over.
                if (mutex.WaitOne(0))
                {
                    createdNew = true;
                }
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
            }
        }

        AppLog.Info(createdNew
            ? "Single-instance lock acquired; this is the primary instance."
            : "Another instance is already running.");

        return new SingleInstanceService(mutex, createdNew, pipeName)
        {
            IsPrimary = createdNew
        };
    }

    public void StartServer(Action activationHandler)
    {
        ArgumentNullException.ThrowIfNull(activationHandler);

        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SingleInstanceService));
            }

            if (!IsPrimary)
            {
                throw new InvalidOperationException("Only the primary instance can start the activation server.");
            }

            if (_serverTask is not null)
            {
                throw new InvalidOperationException("The activation server has already been started.");
            }

            _activationHandler = activationHandler;
            _cancellation = new CancellationTokenSource();
            _serverTask = Task.Run(() => ServerLoopAsync(_cancellation.Token));
        }
    }

    public bool TryActivateExisting(TimeSpan timeout)
    {
        if (IsPrimary)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var connectTimeout = (int)Math.Clamp(remaining.TotalMilliseconds, 100, 750);

            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);

                client.Connect(connectTimeout);

                using var writer = new StreamWriter(client, new UTF8Encoding(false), 256, leaveOpen: false)
                {
                    AutoFlush = true
                };

                writer.WriteLine(ActivationMessage);
                AppLog.Info("Sent activation request to the primary instance.");
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                lastError = ex;
            }

            Thread.Sleep(100);
        }

        AppLog.Error("Failed to contact the primary instance for activation.", lastError);
        return false;
    }

    private async Task ServerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;

            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                lock (_gate)
                {
                    _activeServer = server;
                }

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(
                    server,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 256,
                    leaveOpen: true);

                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                readTimeout.CancelAfter(ServerReadTimeout);
                var line = await reader.ReadLineAsync(readTimeout.Token).ConfigureAwait(false);

                if (string.Equals(line?.Trim(), ActivationMessage, StringComparison.Ordinal))
                {
                    Action? handler;
                    lock (_gate)
                    {
                        handler = _activationHandler;
                    }

                    try
                    {
                        handler?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("Activation handler failed.", ex);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                AppLog.Error("Timed out while reading an activation request.");
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                AppLog.Error("Named-pipe activation server error.", ex);

                try
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_activeServer, server))
                    {
                        _activeServer = null;
                    }
                }

                server?.Dispose();
            }
        }
    }

    private static string GetUserAndSessionKey()
    {
        string userKey;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            userKey = identity.User?.Value ?? Environment.UserName;
        }
        catch
        {
            userKey = Environment.UserName;
        }

        userKey = string.Concat(userKey.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));

        return $"{userKey}.{Process.GetCurrentProcess().SessionId}";
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        Task? serverTask;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _cancellation;
            serverTask = _serverTask;
            _cancellation = null;
            _serverTask = null;
            _activationHandler = null;

            try
            {
                cancellation?.Cancel();
            }
            catch
            {
                // Best effort shutdown.
            }

            try
            {
                _activeServer?.Dispose();
            }
            catch
            {
                // Best effort shutdown.
            }

            _activeServer = null;
        }

        if (serverTask is not null)
        {
            try
            {
                serverTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                AppLog.Error("Activation server did not stop cleanly.", ex);
            }
        }

        cancellation?.Dispose();

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to release the single-instance mutex.", ex);
            }
        }

        _mutex.Dispose();
        AppLog.Info("Single-instance service disposed.");
    }
}
