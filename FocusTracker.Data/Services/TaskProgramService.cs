using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FocusTracker.Data.Services
{
    public class TaskProgramService : ITaskProgramService
    {
        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;

        public TaskProgramService(IDbContextFactory<FocusTrackerDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public List<TaskProgram> GetByTaskId(int taskId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TaskPrograms.AsNoTracking().Where(tp => tp.TaskId == taskId).ToList();
        }

        public void Add(TaskProgram taskProgram)
        {
            using var db = _dbFactory.CreateDbContext();
            db.TaskPrograms.Add(taskProgram);
            db.SaveChanges();
        }

        public void DeleteByTaskId(int taskId)
        {
            using var db = _dbFactory.CreateDbContext();
            var items = db.TaskPrograms.Where(tp => tp.TaskId == taskId).ToList();
            db.TaskPrograms.RemoveRange(items);
            db.SaveChanges();
        }

        // ✅ Новый метод для загрузки программ задачи
        public async Task<List<TrackedProgram>> GetProgramsForTaskAsync(int taskId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.TaskPrograms
                .Where(tp => tp.TaskId == taskId)
                .Select(tp => tp.Program)
                .AsNoTracking()
                .ToListAsync();
        }

        public List<TaskProgram> GetAll()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TaskPrograms.AsNoTracking().ToList();
        }
    }
}
