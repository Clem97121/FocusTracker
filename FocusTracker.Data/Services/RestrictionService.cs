using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FocusTracker.Data.Services
{
    public class RestrictionService : IRestrictionService
    {
        private readonly FocusTrackerDbContext _context;

        public RestrictionService(FocusTrackerDbContext context)
        {
            _context = context;
        }

        // Метод для получения всех ограничений
        public async Task<List<Restriction>> GetAllAsync()
        {
            return await _context.Restrictions
                .Include(r => r.Rules)
                .Include(r => r.Targets)
                    .ThenInclude(t => t.Program) // ✅ ВАЖНО: загружаем связанную программу
                .ToListAsync();
        }


        public async Task CreateAsync(Restriction restriction)
        {
            _context.Restrictions.Add(restriction);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Restriction restriction)
        {
            _context.Restrictions.Update(restriction);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int restrictionId)
        {
            var restriction = await _context.Restrictions.FindAsync(restrictionId);
            if (restriction != null)
            {
                _context.Restrictions.Remove(restriction);
                await _context.SaveChangesAsync();
            }
        }
    }
}
