using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Services;

public class SkillCategoryService : ISkillCategoryService
{
    private readonly FocusTrackerDbContext _db;

    public SkillCategoryService(FocusTrackerDbContext db)
    {
        _db = db;
    }

    public List<SkillCategory> GetAll() =>
        _db.SkillCategories.OrderBy(c => c.SortOrder).ToList();

    public SkillCategory GetById(int id) =>
        _db.SkillCategories.FirstOrDefault(c => c.Id == id);

    public void Add(SkillCategory category)
    {
        _db.SkillCategories.Add(category);
        _db.SaveChanges();
    }

    public void Update(SkillCategory category)
    {
        _db.SkillCategories.Update(category);
        _db.SaveChanges();
    }

    public void Delete(int id)
    {
        var category = _db.SkillCategories.Find(id);
        if (category != null)
        {
            _db.SkillCategories.Remove(category);
            _db.SaveChanges();
        }
    }
    public async Task<List<SkillCategory>> GetAllAsync()
    {
        return await _db.SkillCategories.OrderBy(c => c.SortOrder).ToListAsync();
    }
}
