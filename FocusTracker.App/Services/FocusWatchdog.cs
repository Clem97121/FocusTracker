using FocusTracker.App.Helpers;
using FocusTracker.Domain;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace FocusTracker.App.Services
{
    // Сторож фокус-режиму: окремий процес (той самий exe з --watchdog), який перезапускає FocusTracker,
    // якщо його примусово закрили до кінця фокусу. Застосунок, своєю чергою, перезапускає сторожа.
    // Під час звичайного завершення (оновлення, вихід, вимкнення Windows) сторож зупиняється в OnExit.
    public class FocusWatchdog
    {
        public const string WatchArg = "--watchdog";
        public const string RestartedArg = "--restarted-in-focus";

        private readonly FocusMode _focus;
        private readonly object _lock = new();
        private Process? _process;

        public FocusWatchdog(FocusMode focus)
        {
            _focus = focus;
            _focus.Changed += Update;
        }

        // Новий фокус-режим чи новий кінець — сторож перезапускається з новим часом
        public void Update()
        {
            Stop();
            if (_focus.IsActive) Start();
        }

        private void Start()
        {
            lock (_lock)
            {
                if (_focus.Until is not { } until || Environment.ProcessPath is not { } exe) return;
                try
                {
                    var process = Process.Start(new ProcessStartInfo(exe,
                        $"{WatchArg} {Environment.ProcessId} {until.ToUniversalTime().Ticks}") { UseShellExecute = false })!;
                    process.EnableRaisingEvents = true;
                    // Сторожа закрили, а фокус триває — запускаємо знову
                    process.Exited += (sender, _) =>
                    {
                        if (ReferenceEquals(sender, _process) && _focus.IsActive) Update();
                    };
                    _process = process;
                }
                catch (Exception ex)
                {
                    AppLog.Error("Не вдалося запустити сторожа фокус-режиму", ex);
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                var process = _process;
                _process = null;
                if (process == null) return;
                try
                {
                    if (!process.HasExited) process.Kill();
                }
                catch (Exception ex)
                {
                    AppLog.Error("Не вдалося зупинити сторожа фокус-режиму", ex);
                }
                process.Dispose();
            }
        }

        // Код самого сторожа: чекає завершення застосунку й перезапускає його, якщо фокус ще не скінчився
        public static void Run(string[] args)
        {
            if (args.Length < 3
                || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
                || !long.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var untilTicks))
                return;

            try
            {
                using var app = Process.GetProcessById(pid);
                app.WaitForExit();
            }
            catch (ArgumentException)
            {
                // Застосунок уже завершився
            }

            if (DateTime.UtcNow.Ticks >= untilTicks || Environment.ProcessPath is not { } exe) return;
            // М'ютекс єдиної копії звільняється не миттєво
            Thread.Sleep(TimeSpan.FromSeconds(2));
            Process.Start(new ProcessStartInfo(exe, RestartedArg) { UseShellExecute = false });
        }
    }
}
