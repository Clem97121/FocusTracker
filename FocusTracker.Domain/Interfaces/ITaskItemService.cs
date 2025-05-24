using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FocusTracker.Domain.Models;

namespace FocusTracker.Domain.Interfaces
{
    public interface ITaskItemService
    {
        List<TaskItem> GetAll();
        List<TaskItem> GetBySkillId(int skillId);
        TaskItem? GetById(int id);
        TaskItem Add(TaskItem task);
        void Update(TaskItem task);
        void Delete(int id);
        void MarkCompleted(int id);
    }
}
