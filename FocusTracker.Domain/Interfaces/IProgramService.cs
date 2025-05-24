using FocusTracker.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Interfaces
{
    public interface IProgramService
    {
        Task<List<TrackedProgram>> GetAllAsync();
        Task UpdateAsync(TrackedProgram program);
        void Add(TrackedProgram program);

        Task UpdateDisplayNameIfNeededAsync(string identifier, string displayName);
        Task UpdateIconIfNeededAsync(string identifier, byte[] iconBytes);

        // ✅ добавляем новый метод
        Task UpdateDisplayInfoIfNeededAsync(TrackedProgram program);
    }
}
