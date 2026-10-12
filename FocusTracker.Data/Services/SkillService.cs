using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Services;

public class SkillService : ISkillService
{
    private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;

    public SkillService(IDbContextFactory<FocusTrackerDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public List<Skill> GetAll()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Skills.AsNoTracking().ToList();
    }

    public List<Skill> GetByCategoryId(int categoryId)
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Skills.AsNoTracking().Where(s => s.CategoryId == categoryId).ToList();
    }

    public void Add(Skill skill)
    {
        using var db = _dbFactory.CreateDbContext();
        db.Skills.Add(skill);
        db.SaveChanges();
    }

    // Оновлюємо лише назву/категорію: XP і рівень змінює тільки AddXp,
    // щоб застаріла копія з UI не перезаписала нараховане
    public void Update(Skill skill)
    {
        using var db = _dbFactory.CreateDbContext();
        var existing = db.Skills.Find(skill.Id);
        if (existing == null) return;

        existing.Name = skill.Name;
        existing.CategoryId = skill.CategoryId;
        db.SaveChanges();
    }

    public void Delete(int id)
    {
        using var db = _dbFactory.CreateDbContext();
        var skill = db.Skills.Include(s => s.Tasks).FirstOrDefault(s => s.Id == id);
        if (skill != null)
        {
            // Удаляем связанные задачи
            if (skill.Tasks != null)
                db.TaskItems.RemoveRange(skill.Tasks);

            db.Skills.Remove(skill);
            db.SaveChanges();
        }
    }

    public void AddXp(int skillId, int amount)
    {
        using var db = _dbFactory.CreateDbContext();
        var skill = db.Skills.Find(skillId);
        if (skill == null) return;

        skill.Xp += amount;

        // Простий алгоритм підвищення рівня
        while (skill.Xp >= GetXpForNextLevel(skill.Level))
        {
            skill.Xp -= GetXpForNextLevel(skill.Level);
            skill.Level++;
        }

        db.SaveChanges();
    }

    // Кожен новий рівень вимагає 100 * рівень XP (та сама формула, що й Skill.XpToNextLevel)
    private int GetXpForNextLevel(int level) => Skill.XpForLevel(level);
}
