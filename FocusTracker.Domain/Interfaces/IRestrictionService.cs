using FocusTracker.Domain.Models;

namespace FocusTracker.Domain.Interfaces
{
    public interface IRestrictionService
    {
        Task<List<Restriction>> GetAllAsync();  // Добавляем асинхронный метод для получения всех ограничений
        Task CreateAsync(Restriction restriction);
        Task UpdateAsync(Restriction restriction);
        Task DeleteAsync(int restrictionId);
    }
}
