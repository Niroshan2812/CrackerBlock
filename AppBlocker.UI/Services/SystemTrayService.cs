using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppBlocker.UI.Services
{
    public class SystemTrayService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _headerItem;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _openItem;
        private readonly ToolStripMenuItem _pauseSubMenu;
        private readonly ToolStripMenuItem _resumeItem;
        private readonly ToolStripMenuItem _lockItem;
        private readonly ToolStripMenuItem _exitItem;

        private Icon? _currentIcon;
        private IntPtr _currentHicon = IntPtr.Zero;
        private bool _disposed = false;

        public event Action? OpenDashboardRequested;
        public event Action<int>? PauseRequested;
        public event Action? ResumeRequested;
        public event Action? LockRequested;
        public event Action? ExitRequested;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public SystemTrayService()
        {
            _contextMenu = new ContextMenuStrip
            {
                RenderMode = ToolStripRenderMode.System,
                ShowImageMargin = false
            };

            // 1. Header (Title)
            _headerItem = new ToolStripMenuItem("🛡️ AppBlocker Guardian")
            {
                Enabled = false,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            // 2. Status item
            _statusItem = new ToolStripMenuItem("● Protection: Active")
            {
                Enabled = false,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };

            // 3. Open Dashboard
            _openItem = new ToolStripMenuItem("🖥️ Open Dashboard", null, (s, e) => OpenDashboardRequested?.Invoke())
            {
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            // 4. Quick Pause Submenu
            _pauseSubMenu = new ToolStripMenuItem("⏸️ Quick Pause");
            _pauseSubMenu.DropDownItems.Add("15 Minutes", null, (s, e) => PauseRequested?.Invoke(15));
            _pauseSubMenu.DropDownItems.Add("30 Minutes", null, (s, e) => PauseRequested?.Invoke(30));
            _pauseSubMenu.DropDownItems.Add("1 Hour", null, (s, e) => PauseRequested?.Invoke(60));
            _pauseSubMenu.DropDownItems.Add("Indefinite", null, (s, e) => PauseRequested?.Invoke(0));

            // 5. Resume
            _resumeItem = new ToolStripMenuItem("▶️ Resume Protection", null, (s, e) => ResumeRequested?.Invoke())
            {
                Visible = false
            };

            // 6. Lock UI
            _lockItem = new ToolStripMenuItem("🔒 Lock Screen", null, (s, e) => LockRequested?.Invoke());

            // 7. Exit (PIN Guarded)
            _exitItem = new ToolStripMenuItem("❌ Exit Guardian UI...", null, (s, e) => ExitRequested?.Invoke());

            _contextMenu.Items.Add(_headerItem);
            _contextMenu.Items.Add(_statusItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_openItem);
            _contextMenu.Items.Add(_pauseSubMenu);
            _contextMenu.Items.Add(_resumeItem);
            _contextMenu.Items.Add(_lockItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_exitItem);

            _notifyIcon = new NotifyIcon
            {
                Text = "AppBlocker Guardian: Active",
                ContextMenuStrip = _contextMenu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => OpenDashboardRequested?.Invoke();
            _notifyIcon.Click += (s, e) =>
            {
                if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
                {
                    OpenDashboardRequested?.Invoke();
                }
            };

            SetIcon(isPaused: false);
        }

        public void UpdateState(bool isPaused, string? customStatus = null)
        {
            if (isPaused)
            {
                _statusItem.Text = customStatus != null ? $"⏸️ {customStatus}" : "⏸️ Protection: Paused";
                _notifyIcon.Text = "AppBlocker Guardian: Paused";
                _pauseSubMenu.Visible = false;
                _resumeItem.Visible = true;
                SetIcon(isPaused: true);
            }
            else
            {
                _statusItem.Text = customStatus != null ? $"● {customStatus}" : "● Protection: Active";
                _notifyIcon.Text = "AppBlocker Guardian: Active (Enforcing)";
                _pauseSubMenu.Visible = true;
                _resumeItem.Visible = false;
                SetIcon(isPaused: false);
            }
        }

        public void ShowNotification(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 3000)
        {
            try
            {
                _notifyIcon.ShowBalloonTip(timeoutMs, title, text, icon);
            }
            catch { }
        }

        private void SetIcon(bool isPaused)
        {
            try
            {
                // Clean previous native icon handle if any
                if (_currentHicon != IntPtr.Zero)
                {
                    DestroyIcon(_currentHicon);
                    _currentHicon = IntPtr.Zero;
                }

                using var bmp = new Bitmap(32, 32);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    var shieldBrush = isPaused ? new SolidBrush(Color.FromArgb(245, 158, 11)) : new SolidBrush(Color.FromArgb(139, 92, 246));
                    var borderPen = new Pen(isPaused ? Color.FromArgb(217, 119, 6) : Color.FromArgb(6, 182, 212), 2f);

                    // Draw Shield path
                    using var path = new GraphicsPath();
                    path.AddLine(16, 2, 28, 6);
                    path.AddLine(28, 6, 28, 18);
                    path.AddBezier(28, 18, 24, 27, 16, 30, 16, 30);
                    path.AddBezier(16, 30, 8, 27, 4, 18, 4, 18);
                    path.AddLine(4, 18, 4, 6);
                    path.CloseFigure();

                    g.FillPath(shieldBrush, path);
                    g.DrawPath(borderPen, path);

                    // Indicator icon in center of shield
                    if (isPaused)
                    {
                        // Pause bars
                        using var barBrush = new SolidBrush(Color.White);
                        g.FillRectangle(barBrush, 11, 11, 3, 9);
                        g.FillRectangle(barBrush, 18, 11, 3, 9);
                    }
                    else
                    {
                        // Checkmark
                        using var checkPen = new Pen(Color.White, 2.5f);
                        checkPen.StartCap = LineCap.Round;
                        checkPen.EndCap = LineCap.Round;
                        g.DrawLine(checkPen, 10, 15, 14, 20);
                        g.DrawLine(checkPen, 14, 20, 22, 11);
                    }

                    shieldBrush.Dispose();
                    borderPen.Dispose();
                }

                _currentHicon = bmp.GetHicon();
                _currentIcon = Icon.FromHandle(_currentHicon);
                _notifyIcon.Icon = _currentIcon;
            }
            catch { }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _contextMenu.Dispose();

                if (_currentHicon != IntPtr.Zero)
                {
                    DestroyIcon(_currentHicon);
                    _currentHicon = IntPtr.Zero;
                }
            }
        }
    }
}
