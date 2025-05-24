using FocusTracker.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Interfaces
{
    public interface ISkillCategoryService
    {
        Task<List<SkillCategory>> GetAllAsync();
        SkillCategory GetById(int id);
        void Add(SkillCategory category);
        void Update(SkillCategory category);
        void Delete(int id);
    }

}
