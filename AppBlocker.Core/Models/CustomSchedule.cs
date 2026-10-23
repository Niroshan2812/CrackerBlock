using System;

namespace AppBlocker.Core.Models
{
    public class CustomSchedule
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StartTime { get; set; } = "09:00"; // HH:mm format (24-hour)
        public string EndTime { get; set; } = "17:00";   // HH:mm format (24-hour)
        public string DaysOfWeek { get; set; } = "Mon,Tue,Wed,Thu,Fri"; // Comma-delimited: Mon,Tue,Wed,Thu,Fri,Sat,Sun
        public bool IsActive { get; set; } = true;

        public bool IsActiveAt(DateTime localTime)
        {
            if (!IsActive) return false;

            // Day-of-week check
            var dayCode = localTime.DayOfWeek switch
            {
                DayOfWeek.Monday => "Mon",
                DayOfWeek.Tuesday => "Tue",
                DayOfWeek.Wednesday => "Wed",
                DayOfWeek.Thursday => "Thu",
                DayOfWeek.Friday => "Fri",
                DayOfWeek.Saturday => "Sat",
                DayOfWeek.Sunday => "Sun",
                _ => string.Empty
            };

            if (!string.IsNullOrWhiteSpace(DaysOfWeek) && 
                !DaysOfWeek.Contains(dayCode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Time-of-day check
            if (TimeSpan.TryParse(StartTime, out var start) && TimeSpan.TryParse(EndTime, out var end))
            {
                var time = localTime.TimeOfDay;
                if (start <= end)
                {
                    // Standard single-day window (e.g. 09:00 to 17:00)
                    return time >= start && time < end;
                }
                else
                {
                    // Overnight window across midnight (e.g. 22:00 to 06:00)
                    return time >= start || time < end;
                }
            }

            return true;
        }
    }
}
