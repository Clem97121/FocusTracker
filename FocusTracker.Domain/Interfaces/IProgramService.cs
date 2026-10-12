using FocusTracker.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Interfaces
{
    public interface IProgramService
    {
        Task<List<TrackedProgram>> GetAllAsync();
        Task UpdateAsync(TrackedProgram program);
        Task AddAsync(TrackedProgram program);

        // Ідентифікатори (імена процесів) програм з позначкою «не враховувати простій»
        Task<HashSet<string>> GetIgnoreIdleIdentifiersAsync();

        Task UpdateDisplayNameIfNeededAsync(string identifier, string displayName);
        Task UpdateIconIfNeededAsync(string identifier, byte[] iconBytes);

        // ✅ добавляем новый метод
        Task UpdateDisplayNameAndIconIfNeededAsync(TrackedProgram program);
    }
}
