using System;
using System.Text.Json;

namespace AppBlocker.Core.IPC
{
    public class IpcRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Command { get; set; } = string.Empty;
        public string? Payload { get; set; }

        public static IpcRequest Create(string command, object? payload = null)
        {
            return new IpcRequest
            {
                Command = command,
                Payload = payload != null ? JsonSerializer.Serialize(payload) : null
            };
        }

        public T? GetPayload<T>()
        {
            if (string.IsNullOrWhiteSpace(Payload))
            {
                return default;
            }
            return JsonSerializer.Deserialize<T>(Payload);
        }
    }

    public class IpcResponse
    {
        public string RequestId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Payload { get; set; }

        public static IpcResponse Ok(string requestId, string? message = null, object? payload = null)
        {
            return new IpcResponse
            {
                RequestId = requestId,
                Success = true,
                Message = message ?? "Success",
                Payload = payload != null ? JsonSerializer.Serialize(payload) : null
            };
        }

        public static IpcResponse Error(string requestId, string errorMessage)
        {
            return new IpcResponse
            {
                RequestId = requestId,
                Success = false,
                Message = errorMessage
            };
        }

        public T? GetPayload<T>()
        {
            if (string.IsNullOrWhiteSpace(Payload))
            {
                return default;
            }
            return JsonSerializer.Deserialize<T>(Payload);
        }
    }

    public class ServiceStatusDto
    {
        public bool IsRunning { get; set; } = true;
        public bool IsBlockingPaused { get; set; }
        public int ActiveAppRuleCount { get; set; }
        public int ActiveWebRuleCount { get; set; }
        public DateTime ServiceStartTime { get; set; }
        public string ProcessName { get; set; } = "AppBlocker.Service";
    }

    public class RuleUpdateDto
    {
        public int RuleId { get; set; }
        public string RuleType { get; set; } = "App"; // "App" or "Web"
        public string Target { get; set; } = string.Empty;
        public string? RedirectUrl { get; set; }
        public bool IsActive { get; set; } = true;
        public string ScheduleType { get; set; } = "Always";
    }

    public class PauseBlockingDto
    {
        public int DurationMinutes { get; set; } // 0 for indefinite until resumed
    }

    public class TemporaryUnlockDto
    {
        public string ProcessName { get; set; } = string.Empty;
        public int DurationMinutes { get; set; } = 15;
    }

    public class TaskManagerStateDto
    {
        public bool IsDisabled { get; set; }
    }

    public class BrowserHardeningDto
    {
        public bool DisableIncognito { get; set; } = true;
        public bool DisableDnsOverHttps { get; set; } = true;
    }
}
