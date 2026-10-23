using System;
using System.Collections.Generic;
using System.Linq;

namespace AppBlocker.Core.Models
{
    public static class ScheduleTypes
    {
        public const string Always = "Always";
        public const string WorkHours = "WorkHours"; // Mon-Fri 09:00 - 17:00
        public const string WeekdaysOnly = "WeekdaysOnly";
        public const string WeekendsOnly = "WeekendsOnly";
        public const string EveningOnly = "EveningOnly"; // 18:00 - 23:00

        public static readonly string[] All = new[]
        {
            Always,
            WorkHours,
            WeekdaysOnly,
            WeekendsOnly,
            EveningOnly
        };

        public static bool IsRuleCurrentlyActive(
            string scheduleType, 
            DateTime localTime, 
            IEnumerable<CustomSchedule>? customSchedules = null, 
            bool isFocusModeActive = false)
        {
            // Focus Mode override: All active rules are enforced 100% of the time during an active focus sprint
            if (isFocusModeActive)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(scheduleType) || scheduleType.Equals(Always, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var dayOfWeek = localTime.DayOfWeek;
            var isWeekday = dayOfWeek >= DayOfWeek.Monday && dayOfWeek <= DayOfWeek.Friday;
            var timeOfDay = localTime.TimeOfDay;

            if (scheduleType.Equals(WorkHours, StringComparison.OrdinalIgnoreCase))
            {
                return isWeekday && timeOfDay >= TimeSpan.FromHours(9) && timeOfDay < TimeSpan.FromHours(17);
            }

            if (scheduleType.Equals(WeekdaysOnly, StringComparison.OrdinalIgnoreCase))
            {
                return isWeekday;
            }

            if (scheduleType.Equals(WeekendsOnly, StringComparison.OrdinalIgnoreCase))
            {
                return !isWeekday;
            }

            if (scheduleType.Equals(EveningOnly, StringComparison.OrdinalIgnoreCase))
            {
                return timeOfDay >= TimeSpan.FromHours(18) && timeOfDay < TimeSpan.FromHours(23);
            }

            // Check if matching a custom schedule by Name or "Custom:{Id}"
            if (customSchedules != null)
            {
                var matchedCustom = customSchedules.FirstOrDefault(c =>
                    c.Name.Equals(scheduleType, StringComparison.OrdinalIgnoreCase) ||
                    scheduleType.Equals($"Custom:{c.Id}", StringComparison.OrdinalIgnoreCase));

                if (matchedCustom != null)
                {
                    return matchedCustom.IsActiveAt(localTime);
                }
            }

            return true;
        }
    }
}
