using System.Windows;
using FocusTracker.Domain.Interfaces;
using FocusTracker.App.Views;

namespace FocusTracker.App.Services
{
    public class MessageBoxNotificationService : INotificationService
    {
        public void ShowMessage(string message, string title = "Нагадування")
        {
            var thread = new Thread(() =>
            {
                var box = new Views.CustomMessageBox(message);
                box.ShowDialog();
            });

            thread.SetApartmentState(ApartmentState.STA); // 💥 главное!
            thread.Start();
        }
    }
}
