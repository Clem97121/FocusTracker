// Domain/Interfaces/ITrackedProgramService.cs
using FocusTracker.Domain.Models;
using System.Collections.Generic;

namespace FocusTracker.Domain.Interfaces
{
    public interface ITrackedProgramService
    {
        List<TrackedProgram> GetAll();
        void Add(TrackedProgram program);
        void Delete(int id);
        void UpdateDisplayNameIfNeeded(string identifier, string displayName);
        Task UpdateDisplayInfoIfNeededAsync(TrackedProgram program);

    }
}
