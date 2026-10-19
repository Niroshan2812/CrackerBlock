using System;

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

        public static bool IsRuleCurrentlyActive(string scheduleType, DateTime localTime)
        {
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

            return true;
        }
    }
}
