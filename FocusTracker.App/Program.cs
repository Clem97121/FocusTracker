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

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
