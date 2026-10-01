using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using OpenDynamic.Core.AgentApprovals;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// High-performance, secure Named Pipe server (protocol v1) for Antigravity approvals.
/// Enforces strictly local current-user-only security, bounded payload limits (256 KB),
/// and zero-logging of sensitive commands or paths (Golden Rules 12 &amp; 13).
/// </summary>
public sealed class AgentApprovalPipeServer : IAsyncDisposable, IDisposable
{
    public const string PipeName = "openDynamic-agent-v1";
    public const int MaxConcurrentClients = 8;
    public const int MaxReadSizeBytes = PipeMessage.MaxPayloadSizeBytes;

    private readonly SemaphoreSlim _concurrencySemaphore = new(MaxConcurrentClients, MaxConcurrentClients);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _activeWorkerTasks = new();
    private readonly object _stateLock = new();

    private bool _isRunning;
    private bool _isDisposed;

    /// <summary>
    /// Callback returning whether the Dynamic Island is currently capable of presenting
    /// approval cards to the user (not hidden, not paused, not in exclusive fullscreen).
    /// If false, requests fail safe immediately to AskNative.
    /// </summary>
    public Func<bool>? CanDisplayRequest { get; set; }

    /// <summary>
    /// Event fired when a valid approval request arrives. The handler must manage presentation
    /// and return the resolved <see cref="ApprovalResponse"/>.
    /// </summary>
    public event Func<ApprovalSessionPolicy, Task<ApprovalResponse>>? RequestReceived;

    /// <summary>
    /// Event fired when an active session is cancelled due to client disconnection.
    /// </summary>
    public event Action<string>? RequestCancelled;

    public bool IsRunning
    {
        get
        {
            lock (_stateLock) return _isRunning;
        }
    }

    /// <summary>
    /// Starts the pipe server listening loop.
    /// </summary>
    public void Start()
    {
        lock (_stateLock)
        {
            if (_isRunning || _isDisposed) return;
            _isRunning = true;
        }

        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        Log.Information("AgentApprovalPipeServer started on pipe: {PipeName}", PipeName);
    }

    /// <summary>
    /// Stops the server and cancels any pending client listeners.
    /// </summary>
    public async Task StopAsync()
    {
        lock (_stateLock)
        {
            if (!_isRunning) return;
            _isRunning = false;
        }

        try
        {
            _cts.Cancel();
        }
        catch
        {
            // Ignore cancellation disposal exceptions
        }

        Task[] pending;
        lock (_stateLock)
        {
            pending = _activeWorkerTasks.ToArray();
            _activeWorkerTasks.Clear();
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore timeouts on shutdown
        }

        Log.Information("AgentApprovalPipeServer stopped.");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _concurrencySemaphore.WaitAsync(ct).ConfigureAwait(false);

                var serverStream = CreateServerStream();
                var workerTask = Task.Run(async () =>
                {
                    try
                    {
                        await serverStream.WaitForConnectionAsync(ct).ConfigureAwait(false);
                        await HandleClientSessionAsync(serverStream, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Normal shutdown
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Unexpected error in agent approval client handler.");
                    }
                    finally
                    {
                        try
                        {
                            if (serverStream.IsConnected)
                            {
                                serverStream.Disconnect();
                            }
                            serverStream.Dispose();
                        }
                        catch
                        {
                            // Ignore cleanup errors
                        }
                        _concurrencySemaphore.Release();
                    }
                }, ct);

                lock (_stateLock)
                {
                    _activeWorkerTasks.Add(workerTask);
                    _activeWorkerTasks.RemoveAll(t => t.IsCompleted);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating named pipe listener stream.");
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }
    }

    private static NamedPipeServerStream CreateServerStream()
    {
        // Enforce CurrentUserOnly & Asynchronous per Golden Rule 13
        return new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            MaxConcurrentClients,
            PipeTransmissionMode.Byte,
            PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
    }

    private async Task HandleClientSessionAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string? activeRequestId = null;

        try
        {
            string? rawMessage = await ReadBoundedLineAsync(stream, MaxReadSizeBytes, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                await SendResponseAsync(stream, ApprovalResponse.AskNative("Mensaje vacío recibido"), ct).ConfigureAwait(false);
                return;
            }

            PipeMessage pipeMessage;
            try
            {
                pipeMessage = PipeMessage.Deserialize(rawMessage);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Rejected invalid IPC message from hook client.");
                await SendResponseAsync(stream, ApprovalResponse.AskNative("Mensaje IPC malformado o no soportado"), ct).ConfigureAwait(false);
                return;
            }

            if (pipeMessage.Type.Equals("ping", StringComparison.OrdinalIgnoreCase))
            {
                var pongMsg = PipeMessage.CreatePong();
                await WriteBoundedLineAsync(stream, pongMsg.Serialize(), ct).ConfigureAwait(false);
                return;
            }

            if (!pipeMessage.Type.Equals("request", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(pipeMessage.Payload))
            {
                await SendResponseAsync(stream, ApprovalResponse.AskNative("Tipo de mensaje no reconocido"), ct).ConfigureAwait(false);
                return;
            }

            ApprovalRequest request;
            try
            {
                request = ApprovalRequest.FromJson(pipeMessage.Payload);
                activeRequestId = request.Id;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse ApprovalRequest payload from hook client.");
                await SendResponseAsync(stream, ApprovalResponse.AskNative("Error al analizar la solicitud"), ct).ConfigureAwait(false);
                return;
            }

            var sessionPolicy = new ApprovalSessionPolicy(request);

            // Fail-Safe: If system cannot display the notification, immediately respond AskNative
            if (CanDisplayRequest != null && !CanDisplayRequest.Invoke())
            {
                var rejectResponse = sessionPolicy.RejectBySystemUnavailable("openDynamic no está visible o disponible");
                await SendResponseAsync(stream, rejectResponse, ct).ConfigureAwait(false);

                Log.Information("Approval delegated to native dialog (island unavailable). RequestId={RequestId}, Tool={Tool}, Risk={Risk}",
                    request.Id, request.ToolName, sessionPolicy.Presentation.Risk);
                return;
            }

            if (RequestReceived == null)
            {
                var defaultResponse = sessionPolicy.RejectBySystemUnavailable("Sin manejador de interfaz registrado");
                await SendResponseAsync(stream, defaultResponse, ct).ConfigureAwait(false);
                return;
            }

            // Monitor client connection state while waiting for user interaction
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var disconnectMonitorTask = MonitorDisconnectionAsync(stream, sessionPolicy, sessionCts.Token);

            ApprovalResponse resolvedResponse;
            try
            {
                resolvedResponse = await RequestReceived.Invoke(sessionPolicy).ConfigureAwait(false);
            }
            finally
            {
                sessionCts.Cancel();
                try
                {
                    await disconnectMonitorTask.ConfigureAwait(false);
                }
                catch
                {
                    // Ignore disconnect monitor cleanup
                }
            }

            await SendResponseAsync(stream, resolvedResponse, ct).ConfigureAwait(false);

            sw.Stop();
            // Golden Rule 13: Zero logs of CommandLine, Cwd, TargetFile or CodeContent!
            Log.Information("Approval resolved. RequestId={RequestId}, Tool={Tool}, Risk={Risk}, Decision={Decision}, Source={Source}, ElapsedMs={ElapsedMs}",
                request.Id, request.ToolName, sessionPolicy.Presentation.Risk, resolvedResponse.Decision, sessionPolicy.ResolutionSource ?? "unknown", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(activeRequestId))
            {
                RequestCancelled?.Invoke(activeRequestId);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error while handling approval session on pipe.");
            if (!string.IsNullOrWhiteSpace(activeRequestId))
            {
                RequestCancelled?.Invoke(activeRequestId);
            }
        }
    }

    private async Task MonitorDisconnectionAsync(NamedPipeServerStream stream, ApprovalSessionPolicy policy, CancellationToken ct)
    {
        try
        {
            var buffer = new byte[1];
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
                if (!stream.IsConnected)
                {
                    policy.CancelByClientDisconnect();
                    RequestCancelled?.Invoke(policy.Request.Id);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on normal session resolution
        }
    }

    private static async Task SendResponseAsync(NamedPipeServerStream stream, ApprovalResponse response, CancellationToken ct)
    {
        var pipeMsg = PipeMessage.CreateResponse(response);
        await WriteBoundedLineAsync(stream, pipeMsg.Serialize(), ct).ConfigureAwait(false);
    }

    private static async Task<string> ReadBoundedLineAsync(NamedPipeServerStream stream, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[1024];
        int totalBytes = 0;

        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read == 0) break;

            for (int i = 0; i < read; i++)
            {
                byte b = buffer[i];
                if (b == (byte)'\n')
                {
                    return Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\r');
                }

                ms.WriteByte(b);
                totalBytes++;

                if (totalBytes > maxBytes)
                {
                    throw new InvalidOperationException($"Incoming message exceeded maximum allowed size of {maxBytes} bytes.");
                }
            }
        }

        return Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\r');
    }

    private static async Task WriteBoundedLineAsync(NamedPipeServerStream stream, string content, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(content + "\n");
        if (bytes.Length > MaxReadSizeBytes)
        {
            throw new InvalidOperationException("Outgoing message exceeded maximum allowed size.");
        }

        await stream.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        await StopAsync().ConfigureAwait(false);
        _concurrencySemaphore.Dispose();
        _cts.Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore sync wait on dispose
        }
        _concurrencySemaphore.Dispose();
        _cts.Dispose();
    }
}
