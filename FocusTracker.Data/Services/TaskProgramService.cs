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
        private readonly FocusTrackerDbContext _db;

        public TaskProgramService(FocusTrackerDbContext db)
        {
            _db = db;
        }

        public List<TaskProgram> GetByTaskId(int taskId)
        {
            return _db.TaskPrograms.Where(tp => tp.TaskId == taskId).ToList();
        }

        public void Add(TaskProgram taskProgram)
        {
            _db.TaskPrograms.Add(taskProgram);
            _db.SaveChanges();
        }

        public void DeleteByTaskId(int taskId)
        {
            var items = _db.TaskPrograms.Where(tp => tp.TaskId == taskId).ToList();
            _db.TaskPrograms.RemoveRange(items);
            _db.SaveChanges();
        }

        // ✅ Новый метод для загрузки программ задачи
        public async Task<List<TrackedProgram>> GetProgramsForTaskAsync(int taskId)
        {
            return await _db.TaskPrograms
                .Where(tp => tp.TaskId == taskId)
                .Include(tp => tp.Program)
                .Select(tp => tp.Program)
                .ToListAsync();
        }
        public List<TaskProgram> GetAll()
        {
            return _db.TaskPrograms.ToList();
        }

    }
}
