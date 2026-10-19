using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AppBlocker.Core.IPC
{
    public class NamedPipeIpcServer : IAsyncDisposable
    {
        private readonly string _pipeName;
        private readonly Func<IpcRequest, Task<IpcResponse>> _handler;
        private readonly Action<string>? _logAction;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private volatile NamedPipeServerStream? _currentWaitingStream;

        public NamedPipeIpcServer(string pipeName, Func<IpcRequest, Task<IpcResponse>> handler, Action<string>? logAction = null)
        {
            _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _logAction = logAction;
        }

        public void Start()
        {
            if (_listenerTask != null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
            _logAction?.Invoke($"NamedPipeIpcServer started on pipe: {_pipeName}");
        }

        public async Task StopAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
            }

            try
            {
                // Connect a short-lived dummy client to cleanly unblock WaitForConnectionAsync
                using var dummyClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
                dummyClient.Connect(100);
            }
            catch { }

            try
            {
                _currentWaitingStream?.Dispose();
            }
            catch { }

            if (_listenerTask != null)
            {
                try
                {
                    await _listenerTask;
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    _logAction?.Invoke($"Exception stopping pipe server: {ex.Message}");
                }
                _listenerTask = null;
            }

            _cts?.Dispose();
            _cts = null;
            _logAction?.Invoke("NamedPipeIpcServer stopped.");
        }

        private async Task ListenLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream? serverStream = null;
                try
                {
                    serverStream = CreateServerStream(_pipeName);
                    _currentWaitingStream = serverStream;

                    using (cancellationToken.Register(() =>
                    {
                        try { serverStream.Dispose(); } catch { }
                    }))
                    {
                        await serverStream.WaitForConnectionAsync(cancellationToken);
                    }

                    _currentWaitingStream = null;

                    // Handle the connected client in a background task so server can accept new connections
                    _ = HandleClientConnectionAsync(serverStream, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    serverStream?.Dispose();
                    break;
                }
                catch (ObjectDisposedException)
                {
                    serverStream?.Dispose();
                    break;
                }
                catch (Exception ex)
                {
                    serverStream?.Dispose();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    _logAction?.Invoke($"Error waiting for pipe connection: {ex.Message}");
                    try
                    {
                        await Task.Delay(200, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                finally
                {
                    _currentWaitingStream = null;
                }
            }
        }

        private async Task HandleClientConnectionAsync(NamedPipeServerStream serverStream, CancellationToken cancellationToken)
        {
            await using (serverStream)
            {
                try
                {
                    using var reader = new StreamReader(serverStream, Encoding.UTF8, leaveOpen: true);
                    using var writer = new StreamWriter(serverStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                    while (serverStream.IsConnected && !cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken);
                        if (line == null)
                        {
                            break; // Client disconnected
                        }

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        IpcRequest? request = null;
                        try
                        {
                            request = JsonSerializer.Deserialize<IpcRequest>(line);
                        }
                        catch (Exception ex)
                        {
                            var errResp = IpcResponse.Error("unknown", $"Malformed JSON request: {ex.Message}");
                            await writer.WriteLineAsync(JsonSerializer.Serialize(errResp));
                            continue;
                        }

                        if (request == null)
                        {
                            var errResp = IpcResponse.Error("unknown", "Empty request payload.");
                            await writer.WriteLineAsync(JsonSerializer.Serialize(errResp));
                            continue;
                        }

                        IpcResponse response;
                        try
                        {
                            response = await _handler(request);
                        }
                        catch (Exception handlerEx)
                        {
                            _logAction?.Invoke($"Handler error for command '{request.Command}': {handlerEx.Message}");
                            response = IpcResponse.Error(request.Id, $"Server handler error: {handlerEx.Message}");
                        }

                        var responseJson = JsonSerializer.Serialize(response);
                        await writer.WriteLineAsync(responseJson);
                        await writer.FlushAsync();
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { /* Client disconnected abruptly */ }
                catch (Exception ex)
                {
                    _logAction?.Invoke($"Exception handling client connection: {ex.Message}");
                }
            }
        }

        private static NamedPipeServerStream CreateServerStream(string pipeName)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var pipeSecurity = new PipeSecurity();

                    // Allow Authenticated Users (standard users in WPF app) Read/Write access
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                        PipeAccessRights.ReadWrite,
                        AccessControlType.Allow));

                    // Allow LocalSystem Full Control
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                        PipeAccessRights.FullControl,
                        AccessControlType.Allow));

                    // Allow Administrators Full Control
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                        PipeAccessRights.FullControl,
                        AccessControlType.Allow));

                    return NamedPipeServerStreamAcl.Create(
                        pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        inBufferSize: 4096,
                        outBufferSize: 4096,
                        pipeSecurity);
                }
            }
            catch
            {
                // Fall back if custom ACL fails
            }

            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }
    }
}
