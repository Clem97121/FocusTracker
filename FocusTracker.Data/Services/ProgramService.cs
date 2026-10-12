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
        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;
        private readonly ITrackedProgramService _trackedProgramService;

        public ProgramService(IDbContextFactory<FocusTrackerDbContext> dbFactory, ITrackedProgramService trackedProgramService)
        {
            _dbFactory = dbFactory;
            _trackedProgramService = trackedProgramService;
        }

        public async Task<List<TrackedProgram>> GetAllAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.TrackedPrograms.AsNoTracking().ToListAsync();
        }

        public async Task UpdateAsync(TrackedProgram program)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Entry(program).State = EntityState.Modified;
            await db.SaveChangesAsync();
        }

        public async Task AddAsync(TrackedProgram program)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            await db.TrackedPrograms.AddAsync(program);
            await db.SaveChangesAsync();
        }

        public async Task UpdateDisplayNameIfNeededAsync(string identifier, string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return;

            await using var db = await _dbFactory.CreateDbContextAsync();
            var program = await db.TrackedPrograms
                .FirstOrDefaultAsync(p => p.Identifier == identifier);

            if (program != null && program.DisplayName != displayName)
            {
                program.DisplayName = displayName;
                await db.SaveChangesAsync();
            }
        }

        public async Task UpdateIconIfNeededAsync(string identifier, byte[] iconBytes)
        {
            if (iconBytes == null || iconBytes.Length == 0) return;

            await using var db = await _dbFactory.CreateDbContextAsync();
            var program = await db.TrackedPrograms
                .FirstOrDefaultAsync(p => p.Identifier == identifier);

            if (program != null && !iconBytes.SequenceEqual(program.IconBytes ?? System.Array.Empty<byte>()))
            {
                program.IconBytes = iconBytes;
                await db.SaveChangesAsync();
            }
        }

        public async Task UpdateDisplayNameAndIconIfNeededAsync(TrackedProgram program)
        {
            await _trackedProgramService.UpdateDisplayInfoIfNeededAsync(program);
        }
    }
}
