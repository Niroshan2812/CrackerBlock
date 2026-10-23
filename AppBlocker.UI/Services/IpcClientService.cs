using System;
using System.Threading.Tasks;
using AppBlocker.Core.IPC;

namespace AppBlocker.UI.Services
{
    public class IpcClientService
    {
        private readonly NamedPipeIpcClient _client;

        public IpcClientService()
        {
            _client = new NamedPipeIpcClient();
        }

        public async Task<bool> IsServiceAvailableAsync()
        {
            return await _client.IsServiceRunningAsync(timeoutMs: 1500);
        }

        public async Task<ServiceStatusDto?> GetStatusAsync()
        {
            return await _client.GetStatusAsync();
        }

        public async Task<IpcResponse> ReloadRulesAsync()
        {
            return await _client.SendCommandAsync(IpcConstants.Commands.ReloadRules);
        }

        public async Task<IpcResponse> PauseBlockingAsync(int minutes = 0)
        {
            return await _client.SendCommandAsync(IpcConstants.Commands.PauseBlocking, new PauseBlockingDto { DurationMinutes = minutes });
        }

        public async Task<IpcResponse> ResumeBlockingAsync()
        {
            return await _client.SendCommandAsync(IpcConstants.Commands.ResumeBlocking);
        }

        public async Task<IpcResponse> TemporaryUnlockAppAsync(string processName, int minutes = 15)
        {
            var dto = new TemporaryUnlockDto
            {
                ProcessName = processName,
                DurationMinutes = minutes
            };
            return await _client.SendCommandAsync(IpcConstants.Commands.TemporaryUnlockApp, dto);
        }

        public async Task<IpcResponse> AddAppRuleAsync(string targetExeOrName, string scheduleType = "Always")
        {
            var dto = new RuleUpdateDto
            {
                RuleType = "App",
                Target = targetExeOrName,
                IsActive = true,
                ScheduleType = scheduleType
            };
            return await _client.SendCommandAsync(IpcConstants.Commands.AddAppRule, dto);
        }

        public async Task<IpcResponse> RemoveAppRuleAsync(int ruleId)
        {
            var dto = new RuleUpdateDto { RuleType = "App", RuleId = ruleId };
            return await _client.SendCommandAsync(IpcConstants.Commands.RemoveAppRule, dto);
        }

        public async Task<IpcResponse> AddWebRuleAsync(string urlOrDomain, string scheduleType = "Always", string? redirectUrl = null)
        {
            var dto = new RuleUpdateDto
            {
                RuleType = "Web",
                Target = urlOrDomain,
                RedirectUrl = redirectUrl,
                IsActive = true,
                ScheduleType = scheduleType
            };
            return await _client.SendCommandAsync(IpcConstants.Commands.AddWebRule, dto);
        }

        public async Task<IpcResponse> RemoveWebRuleAsync(int ruleId)
        {
            var dto = new RuleUpdateDto { RuleType = "Web", RuleId = ruleId };
            return await _client.SendCommandAsync(IpcConstants.Commands.RemoveWebRule, dto);
        }

        public async Task<IpcResponse> FlushDnsAsync()
        {
            return await _client.SendCommandAsync(IpcConstants.Commands.FlushDns);
        }

        public async Task<IpcResponse> SetTaskManagerStateAsync(bool disable)
        {
            var dto = new TaskManagerStateDto { IsDisabled = disable };
            return await _client.SendCommandAsync(IpcConstants.Commands.SetTaskManagerState, dto);
        }

        public async Task<bool> IsTaskManagerDisabledAsync()
        {
            var response = await _client.SendCommandAsync(IpcConstants.Commands.GetTaskManagerState);
            if (response.Success && !string.IsNullOrEmpty(response.Payload))
            {
                var dto = response.GetPayload<TaskManagerStateDto>();
                return dto?.IsDisabled ?? false;
            }
            return false;
        }

        public async Task<IpcResponse> SetBrowserHardeningStateAsync(bool disableIncognito, bool disableDnsOverHttps)
        {
            var dto = new BrowserHardeningDto
            {
                DisableIncognito = disableIncognito,
                DisableDnsOverHttps = disableDnsOverHttps
            };
            return await _client.SendCommandAsync(IpcConstants.Commands.SetBrowserHardeningState, dto);
        }

        public async Task<BrowserHardeningDto> GetBrowserHardeningStateAsync()
        {
            var response = await _client.SendCommandAsync(IpcConstants.Commands.GetBrowserHardeningState);
            if (response.Success && !string.IsNullOrEmpty(response.Payload))
            {
                return response.GetPayload<BrowserHardeningDto>() ?? new BrowserHardeningDto();
            }
            return new BrowserHardeningDto { DisableIncognito = false, DisableDnsOverHttps = false };
        }

        public async Task<WatchdogStatusDto> GetWatchdogStatusAsync()
        {
            var response = await _client.SendCommandAsync(IpcConstants.Commands.GetWatchdogStatus);
            if (response.Success && !string.IsNullOrEmpty(response.Payload))
            {
                return response.GetPayload<WatchdogStatusDto>() ?? new WatchdogStatusDto();
            }
            return new WatchdogStatusDto { IsWatchdogActive = false };
        }

        public async Task<IpcResponse> StartFocusSessionAsync(int durationMinutes, bool isStrictNuclearMode, string sessionName = "Pomodoro Sprint")
        {
            var dto = new StartFocusSessionDto
            {
                DurationMinutes = durationMinutes,
                IsStrictNuclearMode = isStrictNuclearMode,
                SessionName = sessionName
            };
            return await _client.SendCommandAsync(IpcConstants.Commands.StartFocusSession, dto);
        }

        public async Task<IpcResponse> StopFocusSessionAsync(bool force = false)
        {
            return await _client.SendCommandAsync(IpcConstants.Commands.StopFocusSession, new { Force = force });
        }

        public async Task<FocusSessionStatusDto> GetFocusSessionStatusAsync()
        {
            var response = await _client.SendCommandAsync(IpcConstants.Commands.GetFocusSessionStatus);
            if (response.Success && !string.IsNullOrEmpty(response.Payload))
            {
                return response.GetPayload<FocusSessionStatusDto>() ?? new FocusSessionStatusDto();
            }
            return new FocusSessionStatusDto { IsActive = false };
        }

        public async Task<List<CustomScheduleDto>> GetCustomSchedulesAsync()
        {
            var response = await _client.SendCommandAsync(IpcConstants.Commands.GetCustomSchedules);
            if (response.Success && !string.IsNullOrEmpty(response.Payload))
            {
                return response.GetPayload<List<CustomScheduleDto>>() ?? new List<CustomScheduleDto>();
            }
            return new List<CustomScheduleDto>();
        }

        public async Task<IpcResponse> AddCustomScheduleAsync(CustomScheduleDto dto)
        {
            return await _client.SendCommandAsync(IpcConstants.Commands.AddCustomSchedule, dto);
        }

        public async Task<IpcResponse> DeleteCustomScheduleAsync(int id)
        {
            return await _client.SendCommandAsync(IpcConstants.Commands.DeleteCustomSchedule, new { Id = id });
        }

        public async Task<IpcResponse> ToggleCustomScheduleAsync(int id)
        {
            return await _client.SendCommandAsync(IpcConstants.Commands.ToggleCustomSchedule, new { Id = id });
        }
    }
}
