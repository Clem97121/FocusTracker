using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusTracker.Data.Services;

public class SkillService : ISkillService
{
    private readonly FocusTrackerDbContext _db;

    public SkillService(FocusTrackerDbContext db)
    {
        _db = db;
    }

    public List<Skill> GetAll() => _db.Skills.ToList();

    public List<Skill> GetByCategoryId(int categoryId) =>
        _db.Skills.Where(s => s.CategoryId == categoryId).ToList();

    public void Add(Skill skill)
    {
        _db.Skills.Add(skill);
        _db.SaveChanges();
    }

    public void Update(Skill skill)
    {
        _db.Skills.Update(skill);
        _db.SaveChanges();
    }

    public void Delete(int id)
    {
        var skill = _db.Skills.Include(s => s.Tasks).FirstOrDefault(s => s.Id == id);
        if (skill != null)
        {
            // Удаляем связанные задачи
            if (skill.Tasks != null)
                _db.TaskItems.RemoveRange(skill.Tasks);

            _db.Skills.Remove(skill);
            _db.SaveChanges();
        }
    }

    public void AddXp(int skillId, int amount)
    {
        var skill = _db.Skills.Find(skillId);
        if (skill == null) return;

        skill.Xp += amount;

        // Простий алгоритм підвищення рівня
        while (skill.Xp >= GetXpForNextLevel(skill.Level))
        {
            skill.Xp -= GetXpForNextLevel(skill.Level);
            skill.Level++;
        }

        _db.Skills.Update(skill);
        _db.SaveChanges();
    }

    private int GetXpForNextLevel(int level)
    {
        // Наприклад: кожен новий рівень вимагає 100 * рівень XP
        return 100 * level;
    }
}