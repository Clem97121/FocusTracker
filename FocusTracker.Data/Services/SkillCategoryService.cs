using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Services;

public class SkillCategoryService : ISkillCategoryService
{
    private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;

    public SkillCategoryService(IDbContextFactory<FocusTrackerDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public List<SkillCategory> GetAll()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.SkillCategories.AsNoTracking().OrderBy(c => c.SortOrder).ToList();
    }

    public SkillCategory GetById(int id)
    {
        using var db = _dbFactory.CreateDbContext();
        return db.SkillCategories.AsNoTracking().FirstOrDefault(c => c.Id == id)!;
    }

    public void Add(SkillCategory category)
    {
        using var db = _dbFactory.CreateDbContext();
        db.SkillCategories.Add(category);
        db.SaveChanges();
    }

    public void Update(SkillCategory category)
    {
        using var db = _dbFactory.CreateDbContext();
        db.Entry(category).State = EntityState.Modified;
        db.SaveChanges();
    }

    public void Delete(int id)
    {
        using var db = _dbFactory.CreateDbContext();
        var category = db.SkillCategories.Find(id);
        if (category != null)
        {
            db.SkillCategories.Remove(category);
            db.SaveChanges();
        }
    }

    public async Task<List<SkillCategory>> GetAllAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.SkillCategories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync();
    }
}
