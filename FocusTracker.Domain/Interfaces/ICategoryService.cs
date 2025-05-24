using FocusTracker.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FocusTracker.Domain.Interfaces
{
    public interface ICategoryService
    {
        Task<List<SkillCategory>> GetAllCategoriesAsync();
        Task AddCategoryAsync(SkillCategory category);
        Task DeleteCategoryAsync(int id);
        Task UpdateCategoryAsync(SkillCategory category);
    }
}
