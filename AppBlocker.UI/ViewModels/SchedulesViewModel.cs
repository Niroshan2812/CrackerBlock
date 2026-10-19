using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using AppBlocker.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AppBlocker.UI.ViewModels
{
    public class ScheduleDisplayItem
    {
        public string Name { get; set; } = string.Empty;
        public string TimeWindow { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActiveNow { get; set; }
    }

    public partial class SchedulesViewModel : ObservableObject
    {
        private readonly DispatcherTimer _timer;

        [ObservableProperty]
        private string _currentTimeString = string.Empty;

        [ObservableProperty]
        private string _currentDateString = string.Empty;

        [ObservableProperty]
        private ObservableCollection<ScheduleDisplayItem> _scheduleItems = new();

        public SchedulesViewModel()
        {
            UpdateClock();
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => UpdateClock();
            _timer.Start();
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            CurrentTimeString = now.ToString("hh:mm:ss tt");
            CurrentDateString = now.ToString("dddd, MMMM dd, yyyy");

            var items = new ObservableCollection<ScheduleDisplayItem>
            {
                new ScheduleDisplayItem
                {
                    Name = "Always",
                    TimeWindow = "24 / 7",
                    Description = "Rules are constantly enforced without interruption.",
                    IsActiveNow = ScheduleTypes.IsRuleCurrentlyActive(ScheduleTypes.Always, now)
                },
                new ScheduleDisplayItem
                {
                    Name = "Work Hours",
                    TimeWindow = "Mon - Fri, 09:00 AM - 05:00 PM",
                    Description = "Enforced during standard business work hours.",
                    IsActiveNow = ScheduleTypes.IsRuleCurrentlyActive(ScheduleTypes.WorkHours, now)
                },
                new ScheduleDisplayItem
                {
                    Name = "Weekdays Only",
                    TimeWindow = "Monday - Friday (All day)",
                    Description = "Active during weekdays, free during weekends.",
                    IsActiveNow = ScheduleTypes.IsRuleCurrentlyActive(ScheduleTypes.WeekdaysOnly, now)
                },
                new ScheduleDisplayItem
                {
                    Name = "Weekends Only",
                    TimeWindow = "Saturday & Sunday (All day)",
                    Description = "Active exclusively during the weekend.",
                    IsActiveNow = ScheduleTypes.IsRuleCurrentlyActive(ScheduleTypes.WeekendsOnly, now)
                },
                new ScheduleDisplayItem
                {
                    Name = "Evening Only",
                    TimeWindow = "Every day, 06:00 PM - 11:00 PM",
                    Description = "Enforces focus during study or evening hours.",
                    IsActiveNow = ScheduleTypes.IsRuleCurrentlyActive(ScheduleTypes.EveningOnly, now)
                }
            };

            ScheduleItems = items;
        }
    }
}
