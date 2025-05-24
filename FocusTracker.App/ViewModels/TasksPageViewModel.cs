using FocusTracker.Data;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace FocusTracker.App.ViewModels
{
    public class TasksPageViewModel : INotifyPropertyChanged
    {
        private readonly ITaskItemService _taskService;
        private readonly ISkillService _skillService;
        private readonly ITrackedProgramService _programService;
        private readonly ITaskProgramService _taskProgramService;
        private readonly IAppUsageStatService _appUsageStatService;
        private readonly ITaskProgramUsageService _taskProgramUsageService;

        public ObservableCollection<TaskItem> Tasks { get; set; } = new();
        public ObservableCollection<Skill> AvailableSkills { get; set; } = new();
        public ObservableCollection<TrackedProgram> AvailablePrograms { get; set; } = new();
        public ObservableCollection<TrackedProgram> SelectedPrograms { get; set; } = new();
        public ObservableCollection<TaskItem> CompletedTasks { get; set; } = new();
        public ObservableCollection<TaskItem> FilteredCompletedTasks { get; set; } = new();

        private string _newTaskTitle;
        public string NewTaskTitle
        {
            get => _newTaskTitle;
            set { _newTaskTitle = value; OnPropertyChanged(); }
        }

        private string _newTaskDescription;
        public string NewTaskDescription
        {
            get => _newTaskDescription;
            set { _newTaskDescription = value; OnPropertyChanged(); }
        }

        private Skill _selectedSkill;
        public Skill SelectedSkill
        {
            get => _selectedSkill;
            set { _selectedSkill = value; OnPropertyChanged(); }
        }

        private int _newTaskDifficulty = 1;
        public int NewTaskDifficulty
        {
            get => _newTaskDifficulty;
            set { _newTaskDifficulty = value; OnPropertyChanged(); }
        }

        private int _newTaskEstimatedMinutes = 30;
        public int NewTaskEstimatedMinutes
        {
            get => _newTaskEstimatedMinutes;
            set { _newTaskEstimatedMinutes = value; OnPropertyChanged(); }
        }

        public bool IsEditing { get; private set; }
        private TaskItem _editingTask;

        public string AddButtonText => IsEditing ? "Оновити" : "Додати";

        private bool _showHistory;
        public bool ShowHistory
        {
            get => _showHistory;
            set { _showHistory = value; OnPropertyChanged(); }
        }

        private DateTime? _historyFilterDate;
        public DateTime? HistoryFilterDate
        {
            get => _historyFilterDate;
            set { _historyFilterDate = value; OnPropertyChanged(); UpdateFilteredHistory(); }
        }

        private Skill _historyFilterSkill;
        public Skill HistoryFilterSkill
        {
            get => _historyFilterSkill;
            set { _historyFilterSkill = value; OnPropertyChanged(); UpdateFilteredHistory(); }
        }

        private TrackedProgram _historyFilterProgram;
        public TrackedProgram HistoryFilterProgram
        {
            get => _historyFilterProgram;
            set { _historyFilterProgram = value; OnPropertyChanged(); UpdateFilteredHistory(); }
        }

        public TasksPageViewModel(
            ITaskItemService taskService,
            ISkillService skillService,
            ITrackedProgramService programService,
            ITaskProgramService taskProgramService,
            IAppUsageStatService appUsageStatService,
            ITaskProgramUsageService taskProgramUsageService)
        {
            _taskService = taskService;
            _skillService = skillService;
            _programService = programService;
            _taskProgramService = taskProgramService;
            _appUsageStatService = appUsageStatService;
            _taskProgramUsageService = taskProgramUsageService;

            LoadSkills();
            LoadPrograms();
            LoadTasks();
            LoadCompletedTasks();
        }

        public void LoadSkills()
        {
            AvailableSkills.Clear();
            foreach (var skill in _skillService.GetAll())
                AvailableSkills.Add(skill);
        }

        public void LoadPrograms()
        {
            AvailablePrograms.Clear();
            foreach (var program in _programService.GetAll().Where(p => !p.IsHidden))
                AvailablePrograms.Add(program);
        }

        public void LoadTasks()
        {
            Tasks.Clear();
            var skills = _skillService.GetAll();
            var programs = _programService.GetAll();
            var allTasks = _taskService.GetAll().Where(t => !t.Completed).ToList();

            foreach (var task in allTasks)
            {
                task.Skill = skills.FirstOrDefault(s => s.Id == task.SkillId);
                task.Programs = _taskProgramService
                    .GetByTaskId(task.Id)
                    .Select(link => programs.FirstOrDefault(p => p.Id == link.ProgramId))
                    .Where(p => p != null)
                    .ToList();

                Tasks.Add(task);
            }
        }

        public void AddTask()
        {
            if (string.IsNullOrWhiteSpace(NewTaskTitle) || SelectedSkill == null)
                return;

            var task = new TaskItem
            {
                Title = NewTaskTitle,
                Description = NewTaskDescription ?? "",
                SkillId = SelectedSkill.Id,
                CreatedAt = DateTime.Now.ToString("s"),
                Completed = false,
                Difficulty = NewTaskDifficulty,
                EstimatedMinutes = NewTaskEstimatedMinutes
            };

            var savedTask = _taskService.Add(task);
            var todayStats = _appUsageStatService.GetStatsForTodayAsync().Result;

            foreach (var program in SelectedPrograms)
            {
                _taskProgramService.Add(new TaskProgram { TaskId = savedTask.Id, ProgramId = program.Id });

                var stat = todayStats.FirstOrDefault(s => s.AppName == program.Identifier);
                int initialActive = (int)(stat?.ActiveTime.TotalSeconds ?? 0);

                _taskProgramUsageService.AddOrUpdate(new TaskProgramUsage
                {
                    TaskId = savedTask.Id,
                    ProgramId = program.Id,
                    CountedActiveMinutes = 0,
                    InitialActiveSeconds = initialActive
                });
            }

            ResetForm();
            LoadTasks();
        }

        public void EditTask(TaskItem task)
        {
            _editingTask = task;
            IsEditing = true;
            NewTaskTitle = task.Title;
            NewTaskDescription = task.Description;
            SelectedSkill = AvailableSkills.FirstOrDefault(s => s.Id == task.SkillId);
            NewTaskDifficulty = task.Difficulty;
            NewTaskEstimatedMinutes = task.EstimatedMinutes;
            SelectedPrograms.Clear();
            foreach (var p in task.Programs)
                SelectedPrograms.Add(p);
            OnPropertyChanged(nameof(AddButtonText));
        }

        public void SaveTaskChanges()
        {
            if (_editingTask == null) return;

            _editingTask.Title = NewTaskTitle;
            _editingTask.Description = NewTaskDescription;
            _editingTask.SkillId = SelectedSkill?.Id ?? _editingTask.SkillId;
            _editingTask.Difficulty = NewTaskDifficulty;
            _editingTask.EstimatedMinutes = NewTaskEstimatedMinutes;

            _taskService.Update(_editingTask);
            _taskProgramService.DeleteByTaskId(_editingTask.Id);
            foreach (var program in SelectedPrograms)
                _taskProgramService.Add(new TaskProgram { TaskId = _editingTask.Id, ProgramId = program.Id });

            ResetForm();
            LoadTasks();
        }

        public void DeleteTask(TaskItem task)
        {
            _taskProgramService.DeleteByTaskId(task.Id);
            _taskService.Delete(task.Id);
            LoadTasks();
        }

        public void CancelTaskEdit() => ResetForm();
        public void StartNewTask() => ResetForm();

        public async Task<int> CompleteTask(TaskItem task)
        {
            int totalNewActiveSeconds = 0;

            if (task.Completed) return 0;

            var today = DateTime.Today;
            var now = DateTime.Now;
            var statsList = (await _appUsageStatService.GetStatsForDayAsync(today)).ToList();
            var allUsage = _taskProgramUsageService.GetAll();
            var allPrograms = _programService.GetAll().ToList();
            var allTasks = _taskService.GetAll();
            DateTime taskCreated = DateTime.Parse(task.CreatedAt);

            Debug.WriteLine($"🔧 Завершення завдання: {task.Title} (ID {task.Id})");
            Debug.WriteLine($"📅 Створено: {taskCreated}");

            foreach (var program in task.Programs.ToList()) // уникнення помилки модифікації колекції
            {
                Debug.WriteLine($"➡ Програма: {program.DisplayName} ({program.Identifier})");

                var stat = statsList.FirstOrDefault(s => s.AppName == program.Identifier);
                if (stat == null)
                {
                    Debug.WriteLine("  ⛔ Немає статистики за програму.");
                    continue;
                }

                int totalActive = (int)stat.ActiveTime.TotalSeconds;
                var usage = allUsage.FirstOrDefault(u => u.TaskId == task.Id && u.ProgramId == program.Id);
                int initialActive = usage?.InitialActiveSeconds ?? 0;

                Debug.WriteLine($"  📊 Активний час (загалом): {totalActive} сек");
                Debug.WriteLine($"  🟦 Початковий активний час: {initialActive} сек");

                var earlierTaskIds = allTasks
                    .Where(t => DateTime.Parse(t.CreatedAt) < taskCreated)
                    .Select(t => t.Id)
                    .ToHashSet();

                int alreadyUsed = allUsage
                    .Where(u => u.ProgramId == program.Id && earlierTaskIds.Contains(u.TaskId))
                    .Select(u => u.CountedActiveMinutes)
                    .DefaultIfEmpty(0)
                    .Max();

                Debug.WriteLine($"  🔁 Уже використано в інших завданнях: {alreadyUsed} сек");

                int effectiveBase = Math.Max(initialActive, alreadyUsed);
                int newActiveSeconds = totalActive - effectiveBase;

                Debug.WriteLine($"  ✅ Новий активний час: {newActiveSeconds} сек");

                if (newActiveSeconds <= 0)
                {
                    Debug.WriteLine("  ⚠ Новий час ≤ 0 — пропуск.");
                    continue;
                }

                totalNewActiveSeconds += newActiveSeconds;

                _taskProgramUsageService.AddOrUpdate(new TaskProgramUsage
                {
                    TaskId = task.Id,
                    ProgramId = program.Id,
                    CountedActiveMinutes = alreadyUsed + newActiveSeconds,
                    InitialActiveSeconds = initialActive,
                    RecordedAt = now.ToString("s")
                });
            }

            if (totalNewActiveSeconds > 0)
            {
                int earnedXp = totalNewActiveSeconds * task.Difficulty;

                Debug.WriteLine($"🎁 Загалом активного часу: {totalNewActiveSeconds} сек");
                Debug.WriteLine($"🌟 Нараховано досвіду: {earnedXp} XP");

                _skillService.AddXp(task.SkillId, earnedXp);

                // ✅ Зберігаємо досвід і активний час у TaskItem
                task.Completed = true;
                task.EarnedXp = earnedXp;
                task.ActiveSeconds = totalNewActiveSeconds;

                _taskService.Update(task);

                // Додатково — зберігаємо в ExperienceHistory
                var db = App.Services.GetRequiredService<FocusTrackerDbContext>();
                db.ExperienceHistories.Add(new ExperienceHistory
                {
                    TaskId = task.Id,
                    XpEarned = earnedXp,
                    RecordedAt = now.ToString("s")
                });
                db.SaveChanges();

                LoadTasks();
                LoadCompletedTasks();

                return earnedXp;
            }

            Debug.WriteLine("❌ Нуль нового активного часу. Досвід не нараховано.");
            return 0;
        }



        private void ResetForm()
        {
            NewTaskTitle = "";
            NewTaskDescription = "";
            SelectedSkill = null;
            SelectedPrograms.Clear();
            NewTaskDifficulty = 1;
            NewTaskEstimatedMinutes = 30;
            IsEditing = false;
            _editingTask = null;
            OnPropertyChanged(nameof(AddButtonText));
        }

        public void LoadCompletedTasks()
        {
            CompletedTasks.Clear();

            var skills = _skillService.GetAll().ToList();
            var programs = _programService.GetAll().ToList();

            // Забираємо всі завершені завдання, відсортувавши за датою створення зверху вниз
            var allTasks = _taskService.GetAll()
                                       .Where(t => t.Completed)
                                       .OrderByDescending(t => DateTime.TryParse(t.CreatedAt, out var dt) ? dt : DateTime.MinValue)
                                       .ToList();

            foreach (var task in allTasks)
            {
                // Підтягуємо навичку
                task.Skill = skills.FirstOrDefault(s => s.Id == task.SkillId);

                // Підтягуємо програми
                task.Programs = _taskProgramService
                    .GetByTaskId(task.Id)
                    .Select(link => programs.FirstOrDefault(p => p.Id == link.ProgramId))
                    .Where(p => p != null)
                    .ToList();

                // Тепер просто беремо те, що вже записано в БД
                // (поле EarnedXp та ActiveSeconds вже мають значення, оскільки ми їх зберегли в CompleteTask)
                // Якщо по якимось завданням цих значень ще немає — можна підстановити 0
                task.EarnedXp = task.EarnedXp ?? 0;
                task.ActiveSeconds = task.ActiveSeconds ?? 0;

                CompletedTasks.Add(task);
            }

            UpdateFilteredHistory();
        }



        private void UpdateFilteredHistory()
        {
            FilteredCompletedTasks.Clear();

            var filtered = CompletedTasks.AsEnumerable();

            if (HistoryFilterDate.HasValue)
                filtered = filtered.Where(t => DateTime.TryParse(t.CreatedAt, out var date) && date.Date == HistoryFilterDate.Value.Date);

            if (HistoryFilterSkill != null)
                filtered = filtered.Where(t => t.SkillId == HistoryFilterSkill.Id);

            if (HistoryFilterProgram != null)
                filtered = filtered.Where(t => t.Programs.Any(p => p.Id == HistoryFilterProgram.Id));

            foreach (var task in filtered)
                FilteredCompletedTasks.Add(task);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
