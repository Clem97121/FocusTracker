using FocusTracker.Domain.Models;
using System.Collections.Generic;

namespace FocusTracker.Domain.Interfaces
{
    public interface ISkillService
    {
        List<Skill> GetAll();
        List<Skill> GetByCategoryId(int categoryId);
        void Add(Skill skill);
        void Update(Skill skill);
        void Delete(int id);

        // 🔧 добавлено для начисления опыта
        void AddXp(int skillId, int amount);
    }
}
