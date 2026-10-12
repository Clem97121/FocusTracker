using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace FocusTracker.Data.Services
{
    public class TaskItemService : ITaskItemService
    {
        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;

        public TaskItemService(IDbContextFactory<FocusTrackerDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public List<TaskItem> GetAll()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TaskItems.AsNoTracking().ToList();
        }

        public List<TaskItem> GetBySkillId(int skillId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TaskItems.AsNoTracking().Where(t => t.SkillId == skillId).ToList();
        }

        public TaskItem? GetById(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.TaskItems.AsNoTracking().FirstOrDefault(t => t.Id == id);
        }

        // 🔧 ВАЖНО: возвращаем task с присвоенным Id
        public TaskItem Add(TaskItem task)
        {
            using var db = _dbFactory.CreateDbContext();
            db.TaskItems.Add(task);
            db.SaveChanges();
            return task;
        }

        // Копіюємо лише скалярні поля, щоб не зачепити Skill та інші навігації
        public void Update(TaskItem task)
        {
            using var db = _dbFactory.CreateDbContext();
            var existing = db.TaskItems.Find(task.Id);
            if (existing == null) return;

            db.Entry(existing).CurrentValues.SetValues(task);
            db.SaveChanges();
        }

        public void Delete(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var task = db.TaskItems.Find(id);
            if (task != null)
            {
                db.TaskItems.Remove(task);
                db.SaveChanges();
            }
        }

        public void MarkCompleted(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var task = db.TaskItems.Find(id);
            if (task != null)
            {
                task.Completed = true;
                db.SaveChanges();
            }
        }
    }
}
