using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AppBlocker.Core.IPC
{
    public class NamedPipeIpcClient
    {
        private readonly string _pipeName;
        private readonly string _serverName;

        public NamedPipeIpcClient(string pipeName = IpcConstants.PipeName, string serverName = ".")
        {
            _pipeName = pipeName;
            _serverName = serverName;
        }

        public async Task<bool> IsServiceRunningAsync(int timeoutMs = 1500)
        {
            try
            {
                var response = await SendCommandAsync(IpcConstants.Commands.Ping, timeoutMs: timeoutMs);
                return response.Success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<IpcResponse> SendCommandAsync(string command, object? payload = null, int timeoutMs = IpcConstants.DefaultTimeoutMs)
        {
            var request = IpcRequest.Create(command, payload);
            return await SendRequestAsync(request, timeoutMs);
        }

        public async Task<IpcResponse> SendRequestAsync(IpcRequest request, int timeoutMs = IpcConstants.DefaultTimeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await using var clientStream = new NamedPipeClientStream(_serverName, _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                
                await clientStream.ConnectAsync(cts.Token);

                using var reader = new StreamReader(clientStream, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(clientStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var requestJson = JsonSerializer.Serialize(request);
                await writer.WriteLineAsync(requestJson);
                await writer.FlushAsync();

                var responseLine = await reader.ReadLineAsync(cts.Token);
                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    return IpcResponse.Error(request.Id, "Received empty response from background service.");
                }

                var response = JsonSerializer.Deserialize<IpcResponse>(responseLine);
                return response ?? IpcResponse.Error(request.Id, "Failed to parse service response.");
            }
            catch (OperationCanceledException)
            {
                return IpcResponse.Error(request.Id, $"IPC connection or operation timed out after {timeoutMs}ms. Is AppBlocker.Service running?");
            }
            catch (TimeoutException)
            {
                return IpcResponse.Error(request.Id, "IPC operation timed out.");
            }
            catch (Exception ex)
            {
                return IpcResponse.Error(request.Id, $"IPC communication error: {ex.Message}");
            }
        }

        public async Task<ServiceStatusDto?> GetStatusAsync(int timeoutMs = IpcConstants.DefaultTimeoutMs)
        {
            var response = await SendCommandAsync(IpcConstants.Commands.GetStatus, timeoutMs: timeoutMs);
            if (response.Success && !string.IsNullOrEmpty(response.Payload))
            {
                return response.GetPayload<ServiceStatusDto>();
            }
            return null;
        }

        public async Task<bool> ReloadRulesAsync(int timeoutMs = IpcConstants.DefaultTimeoutMs)
        {
            var response = await SendCommandAsync(IpcConstants.Commands.ReloadRules, timeoutMs: timeoutMs);
            return response.Success;
        }

        public async Task<bool> PauseBlockingAsync(int durationMinutes = 0, int timeoutMs = IpcConstants.DefaultTimeoutMs)
        {
            var payload = new PauseBlockingDto { DurationMinutes = durationMinutes };
            var response = await SendCommandAsync(IpcConstants.Commands.PauseBlocking, payload, timeoutMs: timeoutMs);
            return response.Success;
        }

        public async Task<bool> ResumeBlockingAsync(int timeoutMs = IpcConstants.DefaultTimeoutMs)
        {
            var response = await SendCommandAsync(IpcConstants.Commands.ResumeBlocking, timeoutMs: timeoutMs);
            return response.Success;
        }
    }
}
