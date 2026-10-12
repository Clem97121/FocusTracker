using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FocusTracker.Data.Services
{
    public class RestrictionService : IRestrictionService
    {
        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;

        public RestrictionService(IDbContextFactory<FocusTrackerDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // Метод для получения всех ограничений
        public async Task<List<Restriction>> GetAllAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Restrictions
                .Include(r => r.Rules)
                .Include(r => r.Targets)
                    .ThenInclude(t => t.Program) // ✅ ВАЖНО: загружаем связанную программу
                .ToListAsync();
        }

        public async Task CreateAsync(Restriction restriction)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Restrictions.Add(restriction);
            await db.SaveChangesAsync();
        }

        // Правила и цілі замінюються повністю тим, що прийшло з UI
        public async Task UpdateAsync(Restriction restriction)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.Restrictions
                .Include(r => r.Rules)
                .Include(r => r.Targets)
                .FirstOrDefaultAsync(r => r.Id == restriction.Id);
            if (existing == null) return;

            existing.Note = restriction.Note;

            db.RestrictionRules.RemoveRange(existing.Rules);
            existing.Rules = (restriction.Rules ?? new List<RestrictionRule>())
                .Select(r => new RestrictionRule { RuleType = r.RuleType, Value = r.Value })
                .ToList();

            db.RemoveRange(existing.Targets);
            existing.Targets = (restriction.Targets ?? new List<RestrictionToProgram>())
                .Select(t => t.ProgramId)
                .Distinct()
                .Select(programId => new RestrictionToProgram { ProgramId = programId })
                .ToList();

            await db.SaveChangesAsync();
        }

        public async Task DeleteAsync(int restrictionId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var restriction = await db.Restrictions.FindAsync(restrictionId);
            if (restriction != null)
            {
                db.Restrictions.Remove(restriction);
                await db.SaveChangesAsync();
            }
        }
    }
}
