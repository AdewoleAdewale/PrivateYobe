using System;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace YIRS.Services
{
    /// <summary>
    /// Manages user session and auto-logout after inactivity
    /// </summary>
    public class SessionManager
    {
        private static SessionManager _instance;
        private static readonly object _lock = new object();

        private DateTime _lastActivityTime;
        private Timer _sessionTimer;
        private const int SESSION_TIMEOUT_MINUTES = 30;
        private const int CHECK_INTERVAL_SECONDS = 60; // Check every minute
        private bool _isSessionActive = false;

        public static SessionManager Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                        _instance = new SessionManager();
                    return _instance;
                }
            }
        }

        private SessionManager()
        {
            _lastActivityTime = DateTime.Now;
        }

        /// <summary>
        /// Start monitoring user session
        /// </summary>
        public void StartSession()
        {
            if (_isSessionActive)
                return;

            _isSessionActive = true;
            _lastActivityTime = DateTime.Now;

            _sessionTimer = new Timer(CheckSessionTimeout, null,
                TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS),
                TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS));

            System.Diagnostics.Debug.WriteLine("Session started");
        }

        /// <summary>
        /// Stop monitoring session
        /// </summary>
        public void StopSession()
        {
            _isSessionActive = false;
            _sessionTimer?.Dispose();
            _sessionTimer = null;
            System.Diagnostics.Debug.WriteLine("Session stopped");
        }

        /// <summary>
        /// Update last activity time - call this on user interactions
        /// </summary>
        public void UpdateActivity()
        {
            _lastActivityTime = DateTime.Now;
            _ = SecureStorageService.UpdateLastActivityAsync();
        }

        /// <summary>
        /// Check if session has timed out
        /// </summary>
        private void CheckSessionTimeout(object state)
        {
            if (!_isSessionActive)
                return;

            TimeSpan inactiveTime = DateTime.Now - _lastActivityTime;

            System.Diagnostics.Debug.WriteLine($"Session check: Inactive for {inactiveTime.TotalMinutes:F1} minutes");

            if (inactiveTime.TotalMinutes >= SESSION_TIMEOUT_MINUTES)
            {
                System.Diagnostics.Debug.WriteLine("Session timeout - logging out user");
                Device.BeginInvokeOnMainThread(async () => await HandleSessionTimeout());
            }
        }

        /// <summary>
        /// Handle session timeout
        /// </summary>
        private async Task HandleSessionTimeout()
        {
            try
            {
                StopSession();

                // Show timeout message
                await Application.Current.MainPage.DisplayAlert(
                    "Session Expired",
                    "Your session has expired due to inactivity. Please login again.",
                    "OK");

                // Clear session data but keep credentials if "Remember Me" was checked
                App.IsUserLoggedIn = false;

                // Navigate back to login
                Device.BeginInvokeOnMainThread(() =>
                {
                    Application.Current.MainPage = new NavigationPage(new Views.MainPage())
                    {
                        BarBackgroundColor = Color.FromHex("#004225"),
                        BarTextColor = Color.White
                    };
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling session timeout: {ex.Message}");
            }
        }

        /// <summary>
        /// Get remaining session time in minutes
        /// </summary>
        public double GetRemainingSessionTime()
        {
            TimeSpan inactiveTime = DateTime.Now - _lastActivityTime;
            double remainingMinutes = SESSION_TIMEOUT_MINUTES - inactiveTime.TotalMinutes;
            return Math.Max(0, remainingMinutes);
        }

        /// <summary>
        /// Check if session is about to expire (less than 5 minutes remaining)
        /// </summary>
        public bool IsSessionExpiringSoon()
        {
            return GetRemainingSessionTime() < 5;
        }

        /// <summary>
        /// Reset session (extend timeout)
        /// </summary>
        public void ResetSession()
        {
            UpdateActivity();
            System.Diagnostics.Debug.WriteLine("Session reset - timeout extended");
        }
    }
}