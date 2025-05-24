namespace FocusTracker.Domain.Interfaces
{
    public interface INotificationService
    {
        void ShowMessage(string message, string title = "Нагадування");
    }
}
