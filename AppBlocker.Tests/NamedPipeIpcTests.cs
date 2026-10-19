using System;
using System.Threading.Tasks;
using AppBlocker.Core.IPC;
using Xunit;

namespace AppBlocker.Tests
{
    public class NamedPipeIpcTests
    {
        [Fact]
        public async Task NamedPipe_ServerAndClient_ExchangeMessagesSuccessfully()
        {
            var testPipeName = $"Test_Pipe_{Guid.NewGuid():N}";
            bool pingReceived = false;

            // Setup Server
            await using var server = new NamedPipeIpcServer(testPipeName, async (request) =>
            {
                await Task.Yield();
                if (request.Command == IpcConstants.Commands.Ping)
                {
                    pingReceived = true;
                    return IpcResponse.Ok(request.Id, "Pong");
                }

                if (request.Command == "Echo")
                {
                    return IpcResponse.Ok(request.Id, request.Payload);
                }

                return IpcResponse.Error(request.Id, "Unknown command");
            });

            server.Start();
            await Task.Delay(100);

            // Setup Client
            var client = new NamedPipeIpcClient(testPipeName);

            // Test Ping
            var pingResponse = await client.SendCommandAsync(IpcConstants.Commands.Ping, timeoutMs: 3000);
            Assert.True(pingResponse.Success);
            Assert.Equal("Pong", pingResponse.Message);
            Assert.True(pingReceived);

            // Test Echo command
            var echoResponse = await client.SendCommandAsync("Echo", new { Test = "Hello IPC" }, timeoutMs: 3000);
            Assert.True(echoResponse.Success);
            Assert.Contains("Hello IPC", echoResponse.Message ?? "");

            // Stop server
            await server.StopAsync();

            // Client should fail gracefully when server is stopped
            var failResponse = await client.SendCommandAsync(IpcConstants.Commands.Ping, timeoutMs: 1000);
            Assert.False(failResponse.Success);
        }
    }
}
