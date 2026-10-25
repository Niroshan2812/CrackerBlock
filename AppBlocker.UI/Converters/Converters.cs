using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AppBlocker.UI.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value switch
            {
                bool boolVal => boolVal,
                int intVal => intVal > 0,
                long longVal => longVal > 0,
                System.Collections.ICollection coll => coll.Count > 0,
                _ => false
            };
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
            {
                bool b = v == Visibility.Visible;
                return Invert ? !b : b;
            }
            return false;
        }
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : false;
        }
    }

    public class StringNotNullOrEmptyToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasValue = !string.IsNullOrWhiteSpace(value as string);
            if (Invert) hasValue = !hasValue;
            return hasValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EqualityToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return Visibility.Collapsed;
            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EqualityToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is true && parameter != null && int.TryParse(parameter.ToString(), out int val))
            {
                return val;
            }
            return Binding.DoNothing;
        }
    }

    public class DiagnosticStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AppBlocker.Core.Diagnostics.DiagnosticStatus status)
            {
                return status switch
                {
                    AppBlocker.Core.Diagnostics.DiagnosticStatus.Passed => Application.Current.Resources["SuccessBrush"],
                    AppBlocker.Core.Diagnostics.DiagnosticStatus.Warning => Application.Current.Resources["WarningBrush"],
                    AppBlocker.Core.Diagnostics.DiagnosticStatus.Failed => Application.Current.Resources["DangerBrush"],
                    _ => Application.Current.Resources["TextMutedBrush"]
                };
            }
            return Application.Current.Resources["TextMutedBrush"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class DiagnosticStatusToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AppBlocker.Core.Diagnostics.DiagnosticStatus status)
            {
                return status switch
                {
                    AppBlocker.Core.Diagnostics.DiagnosticStatus.Passed => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 16, 185, 129)),
                    AppBlocker.Core.Diagnostics.DiagnosticStatus.Warning => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(35, 245, 158, 11)),
                    AppBlocker.Core.Diagnostics.DiagnosticStatus.Failed => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 239, 68, 68)),
                    _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255))
                };
            }
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class DiagnosticStatusToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AppBlocker.Core.Diagnostics.DiagnosticStatus status)
            {
                return status switch
                {
                    AppBlocker.Core.Diagnostics.DiagnosticStatus.Passed => "✅",
                    AppBlocker.Core.Diagnostics.DiagnosticStatus.Warning => "⚠️",
                    AppBlocker.Core.Diagnostics.DiagnosticStatus.Failed => "❌",
                    AppBlocker.Core.Diagnostics.DiagnosticStatus.Checking => "⏳",
                    _ => "❓"
                };
            }
            return "❓";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
