using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using System.Collections.Generic;
using System.Linq;

namespace FocusTracker.Data.Services
{
    public class TaskItemService : ITaskItemService
    {
        private readonly FocusTrackerDbContext _db;

        public TaskItemService(FocusTrackerDbContext db)
        {
            _db = db;
        }

        public List<TaskItem> GetAll()
        {
            return _db.TaskItems.ToList();
        }

        public List<TaskItem> GetBySkillId(int skillId)
        {
            return _db.TaskItems.Where(t => t.SkillId == skillId).ToList();
        }

        public TaskItem? GetById(int id)
        {
            return _db.TaskItems.FirstOrDefault(t => t.Id == id);
        }

        // 🔧 ВАЖНО: возвращаем task с присвоенным Id
        public TaskItem Add(TaskItem task)
        {
            _db.TaskItems.Add(task);
            _db.SaveChanges();
            return task;
        }

        public void Update(TaskItem task)
        {
            _db.TaskItems.Update(task);
            _db.SaveChanges();
        }

        public void Delete(int id)
        {
            var task = _db.TaskItems.Find(id);
            if (task != null)
            {
                _db.TaskItems.Remove(task);
                _db.SaveChanges();
            }
        }

        public void MarkCompleted(int id)
        {
            var task = _db.TaskItems.Find(id);
            if (task != null)
            {
                task.Completed = true;
                _db.SaveChanges();
            }
        }
    }
}
