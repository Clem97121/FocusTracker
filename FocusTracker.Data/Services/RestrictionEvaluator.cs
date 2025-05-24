using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FocusTracker.Data.Services
{
    public class RestrictionEvaluator : IRestrictionEvaluator
    {
        private readonly FocusTrackerDbContext _db;

        public RestrictionEvaluator(FocusTrackerDbContext db)
        {
            _db = db;
        }

        public bool IsViolated(string appName, TimeSpan activeTime, out string violatedNote)
        {
            violatedNote = null;
            if (TryGetViolatedRestrictions(appName, activeTime, out var notes) && notes.Any())
            {
                violatedNote = notes.First();
                return true;
            }
            return false;
        }

        public bool TryGetViolatedRestrictions(string appName, TimeSpan activeTime, out List<string> violatedNotes)
        {
            violatedNotes = new List<string>();
            var now = DateTime.Now;

            var restrictions = _db.Restrictions
                .Include(r => r.Rules)
                .Include(r => r.Targets)
                    .ThenInclude(link => link.Program)
                .ToList();

            foreach (var restriction in restrictions)
            {
                bool appliesToApp = restriction.Targets
                    .Any(p => p.Program.Identifier == appName);

                if (!appliesToApp)
                    continue;

                foreach (var rule in restriction.Rules)
                {
                    switch (rule.RuleType)
                    {
                        case "max_total_seconds":
                            {
                                if (!int.TryParse(rule.Value, out int maxMinutes))
                                    break;

                                double totalSeconds = 0;

                                foreach (var target in restriction.Targets)
                                {
                                    var programIdentifier = target.Program?.Identifier;
                                    if (string.IsNullOrWhiteSpace(programIdentifier))
                                        continue;

                                    var stat = _db.AppUsageStats
                                        .FirstOrDefault(s => s.AppName == programIdentifier && s.Date == now.Date);

                                    if (stat != null)
                                        totalSeconds += stat.TotalTime.TotalSeconds;
                                }

                                if ((totalSeconds / 60.0) > maxMinutes)
                                {
                                    violatedNotes.Add(restriction.Note + $" (загальний час: {(totalSeconds / 60.0):F1} хв)");
                                }
                                break;
                            }

                        case "time_interval":
                            {
                                var parts = rule.Value.Split('–');
                                if (parts.Length == 2 &&
                                    TimeSpan.TryParse(parts[0], out TimeSpan from) &&
                                    TimeSpan.TryParse(parts[1], out TimeSpan to))
                                {
                                    var currentTime = now.TimeOfDay;
                                    bool inRange = from < to
                                        ? currentTime >= from && currentTime <= to
                                        : currentTime >= from || currentTime <= to; // Перехід через північ
                                    if (inRange)
                                    {
                                        violatedNotes.Add(restriction.Note);
                                    }
                                }
                                break;
                            }

                        case "after_task":
                            {
                                if (int.TryParse(rule.Value, out int requiredMinutes))
                                {
                                    var today = DateTime.Today.ToString("yyyy-MM-dd");

                                    var usedMinutes = _db.TaskProgramUsages
                                        .Include(x => x.Task)
                                        .Where(x => x.Task.Completed && x.RecordedAt != null && x.RecordedAt.StartsWith(today))
                                        .Sum(x => x.CountedActiveMinutes);

                                    if (usedMinutes < requiredMinutes)
                                    {
                                        violatedNotes.Add(restriction.Note +
                                            $" (потрібно {requiredMinutes} хв активного часу у завершених завданнях, зараз: {usedMinutes} хв)");
                                    }
                                }
                                break;
                            }

                        case "after_minutes":
                            {
                                if (int.TryParse(rule.Value, out int minutes))
                                {
                                    var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                                    if (uptime.TotalMinutes < minutes)
                                    {
                                        violatedNotes.Add(restriction.Note + $" (нельзя запускать до {minutes} мин)");
                                    }
                                }
                                break;
                            }
                    }
                }
            }

            return violatedNotes.Any();
        }
    }
}