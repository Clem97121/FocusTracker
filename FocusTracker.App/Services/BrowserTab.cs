using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FocusTracker.App.Services
{
    // Закриття поточної вкладки браузера (Ctrl+W) — для заблокованих сайтів
    public static class BrowserTab
    {
        private const byte VkControl = 0x11;
        private const byte VkW = 0x57;
        private const uint KeyUp = 0x0002;

        // Лише якщо у фокусі досі той самий браузер: інакше Ctrl+W закрив би щось інше
        public static void CloseActive(string browser)
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (!string.Equals(process.ProcessName, browser, StringComparison.OrdinalIgnoreCase)) return;
            }
            catch (ArgumentException)
            {
                return;
            }

            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            keybd_event(VkW, 0, 0, UIntPtr.Zero);
            keybd_event(VkW, 0, KeyUp, UIntPtr.Zero);
            keybd_event(VkControl, 0, KeyUp, UIntPtr.Zero);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    }
}
