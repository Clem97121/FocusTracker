using FocusTracker.Data;
using FocusTracker.Data.Services;
using FocusTracker.Domain;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTracker.App.Jarvis
{
    // Збирає для Claude знімок ситуації: цілі, розклад, статистика всіх пристроїв,
    // що було у фокусі останню годину, задачі, фокус-режим і попередні втручання
    public class JarvisContext
    {
        private readonly IDbContextFactory<FocusTrackerDbContext> _dbFactory;
        private readonly AppUsageStatService _localStats;
        private readonly SettingsStore _settings;
        private readonly RecentActivityLog _recent;
        private readonly FocusMode _focus;
        private readonly ITaskItemService _tasks;
        private readonly ISkillService _skills;

        public JarvisContext(IDbContextFactory<FocusTrackerDbContext> dbFactory, AppUsageStatService localStats,
            SettingsStore settings, RecentActivityLog recent, FocusMode focus, ITaskItemService tasks, ISkillService skills)
        {
            _dbFactory = dbFactory;
            _localStats = localStats;
            _settings = settings;
            _recent = recent;
            _focus = focus;
            _tasks = tasks;
            _skills = skills;
        }

        // Постійна частина: цілі, правила, розклад. Змінюється рідко — добре кешується.
        public string DescribeProfile()
        {
            var s = _settings.Current;
            return $"""
                ## Цілі користувача (за пріоритетом, його словами)
                {(string.IsNullOrWhiteSpace(s.Goals) ? "(не задані)" : s.Goals.Trim())}

                ## Правила користувача для тебе
                {(string.IsNullOrWhiteSpace(s.JarvisRules) ? "(немає)" : s.JarvisRules.Trim())}

                ## Розклад
                Продуктивні години: {s.ProductiveFromHour:00}:00–{s.ProductiveToHour:00}:00. Поза ними — особистий час.
                Фокус-режим вмикати {(s.JarvisMayStartFocus ? "дозволено" : "НЕ дозволено, лише пропонувати")}.
                """;
        }

        // Змінна частина: що відбувається зараз
        public async Task<string> DescribeNowAsync(IReadOnlyList<JarvisNote> recentNotes)
        {
            var now = DateTime.Now;
            var text = new StringBuilder();
            text.AppendLine($"## Зараз: {now.ToString("dddd, dd.MM.yyyy HH:mm", new CultureInfo("uk-UA"))}");
            text.AppendLine($"Пристрій: {_settings.Current.DeviceName}");
            text.AppendLine(_focus.IsActive
                ? $"Фокус-режим увімкнено до {_focus.Until:HH:mm} ({_focus.Reason}); заблоковано: {string.Join(", ", _focus.BlockedApps)}"
                : "Фокус-режим вимкнено.");

            text.AppendLine();
            text.AppendLine("## Останні 60 хвилин на цьому пристрої (програма — заголовок вікна)");
            text.AppendLine(_recent.Describe(TimeSpan.FromMinutes(60)));

            text.AppendLine("## Сьогодні на всіх пристроях");
            text.AppendLine(await DescribeTodayAsync());

            text.AppendLine("## Задачі");
            text.AppendLine(DescribeTasks());

            text.AppendLine("## Твої попередні повідомлення сьогодні");
            text.AppendLine(recentNotes.Count == 0
                ? "(ще не було)"
                : string.Join("\n", recentNotes.TakeLast(8).Select(n => $"{n.At:HH:mm} [{n.Kind}] {n.Text}")));

            return text.ToString();
        }

        private async Task<string> DescribeTodayAsync()
        {
            // Статистика всіх пристроїв незалежно від того, що вибрано на екрані
            var allDevices = new StatsScope();
            var stats = await new CombinedAppUsageStatService(_localStats, _dbFactory, allDevices).GetStatsForTodayAsync();
            var computerTime = await new ComputerTimeService(_dbFactory, allDevices).GetComputerTimeAsync(DateTime.Today, DateTime.Today);

            await using var db = await _dbFactory.CreateDbContextAsync();
            var categories = (await db.TrackedPrograms.AsNoTracking().ToListAsync())
                .GroupBy(p => p.Identifier)
                .ToDictionary(g => g.Key, g => (g.First().DisplayName ?? g.Key, g.First().Category));

            var text = new StringBuilder();
            text.AppendLine($"Час за комп'ютером: {Format(computerTime)}");
            foreach (var group in stats.GroupBy(s => categories.TryGetValue(s.AppName, out var c) ? c.Category : "Суміжні"))
            {
                var total = TimeSpan.FromSeconds(group.Sum(s => s.TotalTime.TotalSeconds));
                text.AppendLine($"{group.Key}: {Format(total)}");
            }
            text.AppendLine("Топ програм:");
            foreach (var stat in stats.OrderByDescending(s => s.TotalTime).Take(12))
            {
                var name = categories.TryGetValue(stat.AppName, out var c) ? c.Item1 : stat.AppName;
                text.AppendLine($"- {name} ({stat.AppName}): {Format(stat.TotalTime)}, активно {Format(stat.ActiveTime)}");
            }
            return text.ToString();
        }

        private string DescribeTasks()
        {
            var skills = _skills.GetAll().ToDictionary(s => s.Id, s => s.Name);
            var tasks = _tasks.GetAll();
            var open = tasks.Where(t => !t.Completed).ToList();
            var doneToday = tasks.Count(t => t.Completed && t.SyncUpdatedAt?.ToLocalTime().Date == DateTime.Today);

            var text = new StringBuilder();
            text.AppendLine($"Виконано сьогодні: {doneToday}. Відкриті:");
            if (open.Count == 0)
                text.AppendLine("(немає відкритих задач)");
            foreach (var task in open.Take(15))
            {
                var skill = skills.TryGetValue(task.SkillId, out var name) ? name : "?";
                text.AppendLine($"- {task.Title} [{skill}], ~{task.EstimatedMinutes} хв, створено {task.DateCreated:dd.MM}");
            }
            return text.ToString();
        }

        private static string Format(TimeSpan t) => $"{(int)t.TotalHours} год {t.Minutes} хв";
    }
}
