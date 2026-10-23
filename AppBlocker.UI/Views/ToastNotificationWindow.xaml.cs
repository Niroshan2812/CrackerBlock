using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AppBlocker.UI.Models;

namespace AppBlocker.UI.Views
{
    public partial class ToastNotificationWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private readonly DispatcherTimer _autoCloseTimer;
        private readonly double _verticalOffset;
        private bool _isClosing = false;

        public ToastNotificationWindow(ToastNotificationModel notification, double verticalOffset = 0)
        {
            InitializeComponent();
            _verticalOffset = verticalOffset;

            ApplyModel(notification);

            _autoCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4.5)
            };
            _autoCloseTimer.Tick += (s, e) => CloseWithAnimation();

            Loaded += ToastNotificationWindow_Loaded;
            MouseEnter += (s, e) => _autoCloseTimer.Stop();
            MouseLeave += (s, e) => _autoCloseTimer.Start();
        }

        private void ApplyModel(ToastNotificationModel notification)
        {
            TitleTextBlock.Text = notification.Title;
            MessageTextBlock.Text = notification.Message;
            IconTextBlock.Text = notification.IconEmoji;
            CategoryTextBlock.Text = notification.CategoryBadge;
            TimeTextBlock.Text = notification.FormattedTime;

            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(notification.AccentColorHex);
                AccentBorderBrush.Color = color;
                IconBgBrush.Color = System.Windows.Media.Color.FromArgb(45, color.R, color.G, color.B);
                CategoryBgBrush.Color = System.Windows.Media.Color.FromArgb(50, color.R, color.G, color.B);
                CategoryTextBlock.Foreground = new SolidColorBrush(color);
            }
            catch
            {
                // Fallback default colors already in XAML
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        private void ToastNotificationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 16;
            Top = workArea.Bottom - ActualHeight - 16 - _verticalOffset;

            if (Resources["SlideInAnimation"] is Storyboard slideIn)
            {
                slideIn.Begin(this);
            }

            _autoCloseTimer.Start();
        }

        private void DismissButton_Click(object sender, RoutedEventArgs e)
        {
            CloseWithAnimation();
        }

        public void CloseWithAnimation()
        {
            if (_isClosing)
            {
                return;
            }
            _isClosing = true;
            _autoCloseTimer.Stop();

            if (Resources["SlideOutAnimation"] is Storyboard slideOut)
            {
                slideOut.Completed += (s, e) => Close();
                slideOut.Begin(this);
            }
            else
            {
                Close();
            }
        }
    }
}
