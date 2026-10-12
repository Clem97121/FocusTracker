using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FocusTracker.Core.Services
{
    // Ознаки того, що користувач дивиться/слухає без введення: програма у фокусі
    // відтворює звук або її вікно розгорнуте на весь екран
    public class MediaActivityDetector
    {
        // Нижче цього рівня вважаємо тишею
        private const float SilencePeak = 0.001f;

        public bool IsMediaActive(string appName) => IsForegroundFullscreen() || IsPlayingAudio(appName);

        // Звук шукаємо за ім'ям процесу, а не pid: у браузерів звук іде з окремого процесу
        public bool IsPlayingAudio(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName) || appName == "Unknown") return false;

            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                Check(enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out var devices));
                try
                {
                    Check(devices.GetCount(out var count));
                    for (uint i = 0; i < count; i++)
                    {
                        Check(devices.Item(i, out var device));
                        try
                        {
                            if (DeviceHasAudibleSession(device, appName))
                                return true;
                        }
                        finally { Marshal.ReleaseComObject(device); }
                    }
                }
                finally { Marshal.ReleaseComObject(devices); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }

            return false;
        }

        private static bool DeviceHasAudibleSession(IMMDevice device, string appName)
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            Check(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var managerObject));
            var manager = (IAudioSessionManager2)managerObject;
            try
            {
                Check(manager.GetSessionEnumerator(out var sessions));
                try
                {
                    Check(sessions.GetCount(out var count));
                    for (int i = 0; i < count; i++)
                    {
                        Check(sessions.GetSession(i, out var session));
                        try
                        {
                            if (session.GetProcessId(out var pid) != 0 || pid == 0) continue;
                            if (((IAudioMeterInformation)session).GetPeakValue(out var peak) != 0 || peak < SilencePeak) continue;
                            if (string.Equals(GetProcessName(pid), appName, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                        finally { Marshal.ReleaseComObject(session); }
                    }
                }
                finally { Marshal.ReleaseComObject(sessions); }
            }
            finally { Marshal.ReleaseComObject(manager); }

            return false;
        }

        private static string? GetProcessName(uint pid)
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                return process.ProcessName;
            }
            catch
            {
                return null;
            }
        }

        // Вікно у фокусі займає весь монітор (відео, гра). Робочий стіл не рахуємо.
        public bool IsForegroundFullscreen()
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero || hWnd == GetShellWindow() || hWnd == GetDesktopWindow()) return false;
            if (!GetWindowRect(hWnd, out var window)) return false;

            var monitor = MonitorFromWindow(hWnd, MonitorDefaultToNearest);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return false;

            var screen = info.rcMonitor;
            return window.Left <= screen.Left && window.Top <= screen.Top
                && window.Right >= screen.Right && window.Bottom >= screen.Bottom
                && !IsShellClass(hWnd);
        }

        private static bool IsShellClass(IntPtr hWnd)
        {
            var name = new System.Text.StringBuilder(64);
            GetClassName(hWnd, name, name.Capacity);
            var className = name.ToString();
            return className is "Progman" or "WorkerW";
        }

        private static void Check(int hr)
        {
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        }

        #region WinAPI

        private const uint DeviceStateActive = 0x1;
        private const int ClsCtxAll = 0x17;
        private const uint MonitorDefaultToNearest = 2;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int maxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private enum EDataFlow { Render = 0 }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            // Методи IAudioSessionManager, потрібні лише для правильного порядку в vtable
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr sessionControl);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr audioVolume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr eventContext);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr eventContext);
            [PreserveSig] int GetGroupingParam(out Guid groupingId);
            [PreserveSig] int SetGroupingParam(IntPtr groupingId, IntPtr eventContext);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float peak);
        }

        #endregion
    }
}
