// IRestrictionNotificationService.cs
namespace FocusTracker.Domain.Interfaces
{
    public interface IRestrictionNotificationService
    {
        Task CheckAndNotifyAsync();
    }
}
