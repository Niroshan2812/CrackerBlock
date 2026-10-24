using System;
using System.Collections.Generic;

namespace AppBlocker.Core.Diagnostics
{
    public enum DiagnosticStatus
    {
        Checking,
        Passed,
        Warning,
        Failed
    }

    public class DiagnosticCheckItem
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Checking;
        public string Details { get; set; } = string.Empty;
        public bool CanAutoRepair { get; set; } = true;
        public bool IsRepaired { get; set; } = false;
        public string? RepairAction { get; set; }
    }

    public class DiagnosticsReport
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public int OverallHealthScore { get; set; } = 100;
        public int PassedCount { get; set; }
        public int WarningCount { get; set; }
        public int FailedCount { get; set; }
        public int TotalCount { get; set; }
        public List<DiagnosticCheckItem> Items { get; set; } = new();

        public void CalculateScore()
        {
            TotalCount = Items.Count;
            PassedCount = 0;
            WarningCount = 0;
            FailedCount = 0;

            foreach (var item in Items)
            {
                switch (item.Status)
                {
                    case DiagnosticStatus.Passed:
                        PassedCount++;
                        break;
                    case DiagnosticStatus.Warning:
                        WarningCount++;
                        break;
                    case DiagnosticStatus.Failed:
                        FailedCount++;
                        break;
                }
            }

            if (TotalCount == 0)
            {
                OverallHealthScore = 100;
                return;
            }

            // Warnings subtract half weight, failures subtract full weight
            var score = ((PassedCount * 1.0) + (WarningCount * 0.5)) / TotalCount * 100.0;
            OverallHealthScore = (int)Math.Clamp(Math.Round(score), 0, 100);
        }
    }

    public class RepairDiagnosticsRequest
    {
        public List<string>? CheckIds { get; set; }
    }

    public class RepairDiagnosticsResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public DiagnosticsReport UpdatedReport { get; set; } = new();
    }
}
