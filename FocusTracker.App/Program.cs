using System;
using Velopack;

namespace FocusTracker.App
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Має бути найпершим: під час встановлення, оновлення й видалення Velopack
            // запускає exe з особливими аргументами й завершує процес тут
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ => App.RemoveFromStartup())
                .Run();

            // Сторож фокус-режиму — та сама програма в окремому процесі, без вікон
            if (args.Length > 0 && args[0] == Services.FocusWatchdog.WatchArg)
            {
                Services.FocusWatchdog.Run(args);
                return;
            }

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
