using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FocusTracker.Data.Services
{
    public class ProgramService : IProgramService
    {
        private readonly FocusTrackerDbContext _db;
        private readonly ITrackedProgramService _trackedProgramService;

        public ProgramService(FocusTrackerDbContext db, ITrackedProgramService trackedProgramService)
        {
            _db = db;
            _trackedProgramService = trackedProgramService;
        }

        public async Task<List<TrackedProgram>> GetAllAsync()
        {
            return await _db.TrackedPrograms.ToListAsync();
        }

        public async Task UpdateAsync(TrackedProgram program)
        {
            _db.TrackedPrograms.Update(program);
            await _db.SaveChangesAsync();
        }

        public void Add(TrackedProgram program)
        {
            _db.TrackedPrograms.Add(program);
            _db.SaveChanges();
        }

        public async Task UpdateDisplayNameIfNeededAsync(string identifier, string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return;

            var program = await _db.TrackedPrograms
                .FirstOrDefaultAsync(p => p.Identifier == identifier);

            if (program != null && program.DisplayName != displayName)
            {
                program.DisplayName = displayName;
                await _db.SaveChangesAsync();
            }
        }

        public async Task UpdateIconIfNeededAsync(string identifier, byte[] iconBytes)
        {
            if (iconBytes == null || iconBytes.Length == 0) return;

            var program = await _db.TrackedPrograms
                .FirstOrDefaultAsync(p => p.Identifier == identifier);

            if (program != null && !iconBytes.SequenceEqual(program.IconBytes ?? System.Array.Empty<byte>()))
            {
                program.IconBytes = iconBytes;
                await _db.SaveChangesAsync();

                program.RaisePropertyChanged(nameof(program.IconBytes));
            }
        }

        // ✅ Новый метод, делегируем в TrackedProgramService
        public async Task UpdateDisplayInfoIfNeededAsync(TrackedProgram program)
        {
            await _trackedProgramService.UpdateDisplayInfoIfNeededAsync(program);
        }
    }
}
