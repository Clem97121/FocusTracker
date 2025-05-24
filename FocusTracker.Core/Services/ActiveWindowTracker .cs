using FocusTracker.Domain.Interfaces;
using FocusTracker.Core.Helpers;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using System.Threading.Tasks;

namespace FocusTracker.Core.Services
{
    public class ActiveWindowTracker
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private readonly System.Timers.Timer _timer;
        private readonly Action<string, TimeSpan> _onTracked;
        private readonly IProgramService _programService;
        private readonly ProgramNameResolverService _resolver;

        private string _currentApp;
        private DateTime _lastCheckTime;

        public ActiveWindowTracker(
            Action<string, TimeSpan> onTracked,
            IProgramService programService,
            ProgramNameResolverService resolver,
            double interval = 1000)
        {
            _onTracked = onTracked;
            _programService = programService;
            _resolver = resolver;

            _timer = new System.Timers.Timer(interval);
            _timer.Elapsed += async (s, e) => await TimerElapsedAsync();
        }

        public void Start()
        {
            _currentApp = GetActiveProcessName();
            _lastCheckTime = DateTime.Now;
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        private async Task TimerElapsedAsync()
        {
            string activeApp = GetActiveProcessName();
            DateTime now = DateTime.Now;
            TimeSpan delta = now - _lastCheckTime;
            _lastCheckTime = now;

            _onTracked?.Invoke(_currentApp, delta);

            if (activeApp != _currentApp)
            {
                string exePath = GetActiveProcessPath();
                string displayName = _resolver.Resolve(activeApp, exePath);

                await _programService.UpdateDisplayNameIfNeededAsync(activeApp, displayName);

                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    var iconBytes = IconExtractor.GetIconBytes(exePath);
                    if (iconBytes != null)
                        await _programService.UpdateIconIfNeededAsync(activeApp, iconBytes);
                }

                _currentApp = activeApp;
            }
        }

        private string GetActiveProcessName()
        {
            IntPtr hWnd = GetForegroundWindow();
            GetWindowThreadProcessId(hWnd, out uint pid);

            try
            {
                var process = Process.GetProcessById((int)pid);
                return process.ProcessName;
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetActiveProcessPath()
        {
            IntPtr hWnd = GetForegroundWindow();
            GetWindowThreadProcessId(hWnd, out uint pid);

            try
            {
                var process = Process.GetProcessById((int)pid);
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }
    }
}
