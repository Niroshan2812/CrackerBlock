using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using AppBlocker.Core.Enforcement;
using AppBlocker.Core.Models;
using Xunit;

namespace AppBlocker.Tests
{
    [SupportedOSPlatform("windows")]
    public class CustomScheduleAndFocusTests
    {
        [Fact]
        public void CustomSchedule_StandardWindow_MatchesCorrectly()
        {
            var schedule = new CustomSchedule
            {
                Name = "Office Hours",
                StartTime = "09:00",
                EndTime = "17:00",
                DaysOfWeek = "Mon,Tue,Wed,Thu,Fri",
                IsActive = true
            };

            // Monday at 10:30 AM -> Active
            var monday1030 = new DateTime(2026, 10, 19, 10, 30, 0); // Monday
            Assert.True(schedule.IsActiveAt(monday1030));

            // Monday at 08:30 AM -> Inactive (before 9am)
            var monday0830 = new DateTime(2026, 10, 19, 8, 30, 0);
            Assert.False(schedule.IsActiveAt(monday0830));

            // Saturday at 11:00 AM -> Inactive (weekend)
            var saturday1100 = new DateTime(2026, 10, 24, 11, 0, 0); // Saturday
            Assert.False(schedule.IsActiveAt(saturday1100));
        }

        [Fact]
        public void CustomSchedule_OvernightWindow_MatchesAcrossMidnight()
        {
            var overnight = new CustomSchedule
            {
                Name = "Night Owl Focus",
                StartTime = "22:00",
                EndTime = "04:00",
                DaysOfWeek = "Mon,Tue,Wed,Thu,Fri,Sat,Sun",
                IsActive = true
            };

            // 23:30 (before midnight) -> Active
            var lateNight = new DateTime(2026, 10, 19, 23, 30, 0);
            Assert.True(overnight.IsActiveAt(lateNight));

            // 02:15 (after midnight) -> Active
            var earlyMorning = new DateTime(2026, 10, 20, 2, 15, 0);
            Assert.True(overnight.IsActiveAt(earlyMorning));

            // 14:00 (afternoon) -> Inactive
            var afternoon = new DateTime(2026, 10, 20, 14, 0, 0);
            Assert.False(overnight.IsActiveAt(afternoon));
        }

        [Fact]
        public void ScheduleTypes_EvaluatesCustomSchedule_Correctly()
        {
            var customSchedules = new List<CustomSchedule>
            {
                new CustomSchedule
                {
                    Id = 42,
                    Name = "Deep Study",
                    StartTime = "14:00",
                    EndTime = "18:00",
                    DaysOfWeek = "Mon,Wed,Fri",
                    IsActive = true
                }
            };

            var wednesday1500 = new DateTime(2026, 10, 21, 15, 0, 0); // Wednesday

            // Matches by Name
            Assert.True(ScheduleTypes.IsRuleCurrentlyActive("Deep Study", wednesday1500, customSchedules));

            // Matches by "Custom:42"
            Assert.True(ScheduleTypes.IsRuleCurrentlyActive("Custom:42", wednesday1500, customSchedules));

            // Thursday 15:00 -> Inactive for this custom schedule
            var thursday1500 = new DateTime(2026, 10, 22, 15, 0, 0);
            Assert.False(ScheduleTypes.IsRuleCurrentlyActive("Deep Study", thursday1500, customSchedules));
        }

        [Fact]
        public void FocusModeOverride_ForcesRuleActive_EvenOutsideSchedule()
        {
            var customSchedules = new List<CustomSchedule>
            {
                new CustomSchedule
                {
                    Name = "Work Hours",
                    StartTime = "09:00",
                    EndTime = "17:00",
                    DaysOfWeek = "Mon,Tue,Wed,Thu,Fri",
                    IsActive = true
                }
            };

            // Sunday midnight -> normally completely inactive
            var sundayMidnight = new DateTime(2026, 10, 25, 0, 30, 0);
            Assert.False(ScheduleTypes.IsRuleCurrentlyActive("Work Hours", sundayMidnight, customSchedules, isFocusModeActive: false));

            // With Focus Mode Active -> immediately returns true!
            Assert.True(ScheduleTypes.IsRuleCurrentlyActive("Work Hours", sundayMidnight, customSchedules, isFocusModeActive: true));
        }

        [Fact]
        public void FocusSessionService_StrictNuclearMode_PreventsStoppingWithoutForce()
        {
            var focus = new FocusSessionService();

            // 1. Initially inactive
            Assert.False(focus.IsActive);
            Assert.False(focus.IsStrictNuclearMode);

            // 2. Start Strict Nuclear Session
            focus.StartSession(durationMinutes: 25, isStrictNuclearMode: true, sessionName: "Nuclear Sprint");
            Assert.True(focus.IsActive);
            Assert.True(focus.IsStrictNuclearMode);

            var status = focus.GetStatus();
            Assert.Equal("Nuclear Sprint", status.SessionName);
            Assert.True(status.RemainingSeconds > 0);
            Assert.True(status.IsStrictNuclearMode);

            // 3. Normal stop must be rejected!
            var stopSuccess = focus.StopSession(force: false);
            Assert.False(stopSuccess);
            Assert.True(focus.IsActive); // Remains active!

            // 4. Force stop terminates session
            var forceStopSuccess = focus.StopSession(force: true);
            Assert.True(forceStopSuccess);
            Assert.False(focus.IsActive);
        }
    }
}
