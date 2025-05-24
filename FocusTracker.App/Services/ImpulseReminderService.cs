using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FocusTracker.App.Services
{
    public class ImpulseReminderService : IImpulseReminderService
    {
        private readonly ITaskItemService _taskService;
        private readonly ITaskProgramService _taskProgramService;
        private readonly IAppUsageStatService _usageService;
        private readonly INotificationService _notifier;

        public ImpulseReminderService(
            ITaskItemService taskService,
            ITaskProgramService taskProgramService,
            IAppUsageStatService usageService,
            INotificationService notifier)
        {
            _taskService = taskService;
            _taskProgramService = taskProgramService;
            _usageService = usageService;
            _notifier = notifier;
        }

        public async Task CheckAndRemindAsync()
        {
            var tasks = _taskService.GetAll().Where(t => !t.Completed).ToList();
            var stats = await _usageService.GetStatsForTodayAsync();
            var now = DateTime.Now;

            foreach (var task in tasks)
            {
                // Загружаем привязанные программы к задаче
                var linkedPrograms = await _taskProgramService.GetProgramsForTaskAsync(task.Id);
                if (linkedPrograms == null || linkedPrograms.Count == 0)
                    continue;

                if (task.LastRemindedAt.HasValue &&
                    (now - task.LastRemindedAt.Value).TotalMinutes < 60)
                    continue;

                // Чи запускався користувач останню годину хоч одну з прив'язаних програм
                var wasUsedRecently = linkedPrograms.Any(p =>
                {
                    var stat = stats.FirstOrDefault(s => s.AppName == p.Identifier);
                    return stat != null && (now - stat.Date).TotalMinutes > 9999;
                });

                if (!wasUsedRecently)
                {
                    task.LastRemindedAt = now;

                    _notifier.ShowMessage(
                        $"🧠 У тебе є завдання «{task.Title}», але пов’язані програми не запускались понад годину. Почни з 2 хвилин!");
                }
            }
        }
    }
}
