namespace AppBlocker.Core.IPC
{
    public static class IpcConstants
    {
        public const string PipeName = "AppBlocker_IPC_Pipe";
        public const int DefaultTimeoutMs = 5000;

        public static class Commands
        {
            public const string Ping = "Ping";
            public const string GetStatus = "GetStatus";
            public const string ReloadRules = "ReloadRules";
            public const string AddAppRule = "AddAppRule";
            public const string RemoveAppRule = "RemoveAppRule";
            public const string ToggleAppRule = "ToggleAppRule";
            public const string AddWebRule = "AddWebRule";
            public const string RemoveWebRule = "RemoveWebRule";
            public const string ToggleWebRule = "ToggleWebRule";
            public const string PauseBlocking = "PauseBlocking";
            public const string ResumeBlocking = "ResumeBlocking";
            public const string TemporaryUnlockApp = "TemporaryUnlockApp";
            public const string FlushDns = "FlushDns";
            public const string SetTaskManagerState = "SetTaskManagerState";
            public const string GetTaskManagerState = "GetTaskManagerState";
            public const string SetBrowserHardeningState = "SetBrowserHardeningState";
            public const string GetBrowserHardeningState = "GetBrowserHardeningState";
            public const string GetWatchdogStatus = "GetWatchdogStatus";
            public const string StartFocusSession = "StartFocusSession";
            public const string StopFocusSession = "StopFocusSession";
            public const string GetFocusSessionStatus = "GetFocusSessionStatus";
            public const string GetCustomSchedules = "GetCustomSchedules";
            public const string AddCustomSchedule = "AddCustomSchedule";
            public const string DeleteCustomSchedule = "DeleteCustomSchedule";
            public const string ToggleCustomSchedule = "ToggleCustomSchedule";
            public const string SetSafeChildModeState = "SetSafeChildModeState";
            public const string GetSafeChildModeState = "GetSafeChildModeState";
        }
    }
}
