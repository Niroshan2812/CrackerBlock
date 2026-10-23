using System;

namespace AppBlocker.Core.Enforcement
{
    public class FocusSessionStatus
    {
        public bool IsActive { get; set; }
        public string SessionName { get; set; } = "Focus Session";
        public int TotalSeconds { get; set; }
        public int RemainingSeconds { get; set; }
        public bool IsStrictNuclearMode { get; set; }
        public DateTime? EndsAt { get; set; }
    }

    /// <summary>
    /// Coordinates interactive Pomodoro focus sessions and Strict "Nuclear" mode lockdown.
    /// When a focus session is active, all rules are enforced immediately regardless of
    /// scheduled time windows. In Strict Nuclear mode, blocking pauses are disallowed.
    /// </summary>
    public class FocusSessionService
    {
        private readonly object _lock = new object();
        private readonly Action<string>? _logger;

        private bool _isActive;
        private string _sessionName = "Pomodoro Sprint";
        private DateTime? _startedAt;
        private DateTime? _endsAt;
        private int _totalSeconds;
        private bool _isStrictNuclearMode;

        public event Action<string>? SessionStarted;
        public event Action<string>? SessionCompleted;

        public FocusSessionService(Action<string>? logger = null)
        {
            _logger = logger;
        }

        public bool IsActive
        {
            get
            {
                lock (_lock)
                {
                    CheckExpiry_NoLock();
                    return _isActive;
                }
            }
        }

        public bool IsStrictNuclearMode
        {
            get
            {
                lock (_lock)
                {
                    CheckExpiry_NoLock();
                    return _isActive && _isStrictNuclearMode;
                }
            }
        }

        public FocusSessionStatus GetStatus()
        {
            lock (_lock)
            {
                CheckExpiry_NoLock();

                int remaining = 0;
                if (_isActive && _endsAt.HasValue)
                {
                    var rem = _endsAt.Value - DateTime.UtcNow;
                    remaining = rem.TotalSeconds > 0 ? (int)rem.TotalSeconds : 0;
                }

                return new FocusSessionStatus
                {
                    IsActive = _isActive,
                    SessionName = _sessionName,
                    TotalSeconds = _totalSeconds,
                    RemainingSeconds = remaining,
                    IsStrictNuclearMode = _isStrictNuclearMode,
                    EndsAt = _endsAt
                };
            }
        }

        public bool StartSession(int durationMinutes, bool isStrictNuclearMode = false, string sessionName = "Pomodoro Sprint")
        {
            if (durationMinutes <= 0) durationMinutes = 25;

            lock (_lock)
            {
                _isActive = true;
                _sessionName = string.IsNullOrWhiteSpace(sessionName) ? "Pomodoro Sprint" : sessionName;
                _startedAt = DateTime.UtcNow;
                _endsAt = DateTime.UtcNow.AddMinutes(durationMinutes);
                _totalSeconds = durationMinutes * 60;
                _isStrictNuclearMode = isStrictNuclearMode;

                var modeStr = isStrictNuclearMode ? " [STRICT NUCLEAR LOCK ACTIVE]" : "";
                _logger?.Invoke($"[FOCUS MODE] Started '{_sessionName}' ({durationMinutes} mins) until {_endsAt.Value.ToLocalTime():T}{modeStr}");
            }

            SessionStarted?.Invoke(_sessionName);
            return true;
        }

        public bool StopSession(bool force = false)
        {
            lock (_lock)
            {
                if (!_isActive) return true;

                // In strict nuclear mode, stopping requires explicit force (or master PIN bypass if permitted)
                if (_isStrictNuclearMode && !force)
                {
                    _logger?.Invoke("[FOCUS MODE] Stop rejected: Strict Nuclear Mode is actively locked down.");
                    return false;
                }

                _isActive = false;
                _startedAt = null;
                _endsAt = null;
                _isStrictNuclearMode = false;
                _logger?.Invoke($"[FOCUS MODE] Focus session '{_sessionName}' ended.");
            }

            SessionCompleted?.Invoke(_sessionName);
            return true;
        }

        private void CheckExpiry_NoLock()
        {
            if (_isActive && _endsAt.HasValue && DateTime.UtcNow >= _endsAt.Value)
            {
                _isActive = false;
                _logger?.Invoke($"[FOCUS MODE] Focus sprint '{_sessionName}' completed automatically!");
                _startedAt = null;
                _endsAt = null;
                _isStrictNuclearMode = false;

                SessionCompleted?.Invoke(_sessionName);
            }
        }
    }
}
