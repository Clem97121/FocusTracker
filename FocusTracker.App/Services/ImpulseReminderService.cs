using FocusTracker.Domain.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace FocusTracker.App.Services
{
    // «Метод двох хвилин»: якщо по відкритій задачі понад годину не відкривалась жодна
    // з пов'язаних програм, нагадуємо почати хоча б на 2 хвилини.
    public class ImpulseReminderService : IImpulseReminderService
    {
        public static readonly TimeSpan IdleThreshold = TimeSpan.FromHours(1);

        private readonly ITaskItemService _taskService;
        private readonly ITaskProgramService _taskProgramService;
        private readonly INotificationService _notifier;

        // Сервіс singleton, тож час останнього нагадування живе весь сеанс
        private readonly ConcurrentDictionary<int, DateTime> _lastRemindedAt = new();

        public ImpulseReminderService(
            ITaskItemService taskService,
            ITaskProgramService taskProgramService,
            INotificationService notifier)
        {
            _taskService = taskService;
            _taskProgramService = taskProgramService;
            _notifier = notifier;
        }

        public async Task CheckAndRemindAsync()
        {
            var now = DateTime.Now;
            var tasks = _taskService.GetAll().Where(t => !t.Completed).ToList();

            foreach (var task in tasks)
            {
                // Щойно створеній задачі даємо годину, перш ніж нагадувати
                if (now - task.DateCreated < IdleThreshold)
                    continue;

                if (_lastRemindedAt.TryGetValue(task.Id, out var lastReminded) && now - lastReminded < IdleThreshold)
                    continue;

                var linkedPrograms = await _taskProgramService.GetProgramsForTaskAsync(task.Id);
                if (linkedPrograms == null || linkedPrograms.Count == 0)
                    continue;

                var wasUsedRecently = linkedPrograms.Any(p => p.LastUsed.HasValue && now - p.LastUsed.Value < IdleThreshold);
                if (wasUsedRecently)
                    continue;

                _lastRemindedAt[task.Id] = now;
                _notifier.ShowMessage(
                    $"🧠 У тебе є завдання «{task.Title}», але пов’язані програми не запускались понад годину. Почни з 2 хвилин!");
            }
        }
    }
}
